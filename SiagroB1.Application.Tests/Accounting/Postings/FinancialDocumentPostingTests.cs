using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Postings;

/// <summary>Títulos: 500/510 na inclusão e 505/515 no cancelamento, só para títulos sem nota fiscal de origem.</summary>
public class FinancialDocumentPostingTests
{
    private static async Task<IUnitOfWork> SeedAsync()
    {
        var db = await AccountingPostingServiceTests.SeedAsync();
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.1.02", Name = "Clientes", Type = LedgerAccountType.Asset });
        db.Context.StandardPostings.AddRange(
            Lp("510", Seq("001", "Fixed", "4.1.01", "Partner.LedgerAccount", "2.1.01", "Document.NetAmount", "Título {DocNumber}")),
            Lp("500", Seq("001", "Fixed", "1.1.02", "Fixed", "2.1.01", "Document.NetAmount")));
        await db.SaveChangesAsync();
        return db;
    }

    private static FinancialDocumentsManualCreateService Manual(IUnitOfWork db) =>
        new(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["F1"] = "FORNECEDOR" }), accounting: Service(db));

    private static FinancialDocument Input(FinancialDirection direction) => new()
    {
        CardCode = "F1", BranchCode = "01", Direction = direction, DocumentDate = Day, DueDate = Day.AddDays(30), NetAmount = 250m,
    };

    private static Task<List<JournalEntry>> Entries(IUnitOfWork db) =>
        db.Context.JournalEntries.AsNoTracking().Include(e => e.Lines).OrderBy(e => e.DocEntry).ToListAsync();

    [Fact]
    public async Task Manual_payable_posts_510()
    {
        var db = await SeedAsync();
        var doc = await Manual(db).ExecuteAsync(Input(FinancialDirection.Payable), "admin");

        var entry = (await Entries(db)).Single();
        Assert.Equal("510", entry.StandardPostingCode);
        Assert.Equal(doc.Key.ToString(), entry.SourceDocumentKey);
        Assert.Equal(250m, entry.Lines.Single().Amount);
        Assert.Equal(Day, entry.PostingDate);
    }

    [Fact]
    public async Task Manual_receivable_posts_500()
    {
        var db = await SeedAsync();
        await Manual(db).ExecuteAsync(Input(FinancialDirection.Receivable), "admin");
        Assert.Equal("500", (await Entries(db)).Single().StandardPostingCode);
    }

    [Theory]
    [InlineData(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.Manual, false)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation, false)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice, false)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.OtherPurchaseInvoice, false)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.Manual, true)]
    [InlineData(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesContractWashout, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.FinancialDocument, true)]
    public void Postable_titles_follow_origin_rule(FinancialDocumentNature nature, FinancialDocumentOrigin origin, bool expected) =>
        Assert.Equal(expected, FinancialDocumentPosting.IsPostable(new FinancialDocument { CardCode = "F1", Nature = nature, OriginType = origin }));

    [Fact]
    public async Task Cancel_manual_posts_515()
    {
        var db = await SeedAsync();
        db.Context.StandardPostings.Add(Lp("515", Seq("001", "Fixed", "2.1.01", "Fixed", "4.1.01", "Document.NetAmount")));
        await db.SaveChangesAsync();
        var doc = await Manual(db).ExecuteAsync(Input(FinancialDirection.Payable), "admin");

        await new FinancialDocumentsCancelService(db, Service(db)).ExecuteAsync(doc.Key, "erro", "admin");

        var entries = await Entries(db);
        Assert.Equal(["510", "515"], entries.Select(e => e.StandardPostingCode));
        Assert.Equal(entries[0].DocEntry, entries[1].ReversalOfDocEntry);
    }

    [Fact]
    public async Task Cancel_of_unposted_title_is_noop()
    {
        var db = await SeedAsync();
        var doc = await new FinancialDocumentsManualCreateService(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["F1"] = "FORNECEDOR" })).ExecuteAsync(Input(FinancialDirection.Payable), "admin");

        await new FinancialDocumentsCancelService(db, Service(db)).ExecuteAsync(doc.Key, "erro", "admin");

        Assert.Empty(await Entries(db));
    }
}
