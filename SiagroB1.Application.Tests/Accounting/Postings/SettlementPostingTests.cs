using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Postings;

/// <summary>Baixas: 520/530 por linha positiva, 527/531 pela linha de estorno, provisório e composição de fora.</summary>
public class SettlementPostingTests
{
    private static async Task<(IUnitOfWork Db, FinancialDocument Title)> SeedAsync(
        FinancialDocumentNature nature = FinancialDocumentNature.Firm, bool cutover = true)
    {
        var db = await AccountingPostingServiceTests.SeedAsync(cutover: cutover);
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.1.01", Name = "Banco", Type = LedgerAccountType.Asset });
        db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl, LedgerAccountCode = "1.1.01",
            BranchCode = "01",
        });
        db.Context.StandardPostings.AddRange(
            Lp("530", Seq("001", "Partner.LedgerAccount", "2.1.01", "FinancialAccount.LedgerAccount", null, "Settlement.Amount", "Pgto {DocNumber}")),
            Lp("531", Seq("001", "FinancialAccount.LedgerAccount", null, "Partner.LedgerAccount", "2.1.01", "Settlement.Amount")));
        var title = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN-1", CardCode = "F1", CardName = "FORNECEDOR", BranchCode = "01",
            Direction = FinancialDirection.Payable, Nature = nature, Status = FinancialDocumentStatus.Open,
            DocumentDate = Day, DueDate = Day.AddDays(30), NetAmount = 500m, Currency = CurrencyType.Brl,
            OriginType = FinancialDocumentOrigin.Manual,
        };
        db.Context.FinancialDocuments.Add(title);
        await db.SaveChangesAsync();
        return (db, title);
    }

    private static Task<FinancialSettlement> Settle(IUnitOfWork db, FinancialDocument title, decimal amount = 200m) =>
        new FinancialDocumentsSettleService(db, Service(db)).ExecuteAsync(title.Key, "BCO1", amount, Day, 0m, 0m, 0m,
            documentReference: null, notes: null, "admin");

    private static Task<List<JournalEntry>> Entries(IUnitOfWork db) =>
        db.Context.JournalEntries.AsNoTracking().Include(e => e.Lines).OrderBy(e => e.DocEntry).ToListAsync();

    [Fact]
    public async Task Settle_payable_posts_530_with_bank_account()
    {
        var (db, title) = await SeedAsync();
        var settlement = await Settle(db, title);

        var entry = (await Entries(db)).Single();
        Assert.Equal("530", entry.StandardPostingCode);
        Assert.Equal(settlement.Key.ToString(), entry.SourceDocumentKey);
        Assert.Equal(("2.1.01", "1.1.01"), (entry.Lines.Single().DebitAccountCode, entry.Lines.Single().CreditAccountCode));
        Assert.Equal(200m, entry.Lines.Single().Amount);
        Assert.Equal("Pgto FN-1", entry.Lines.Single().History);
    }

    [Fact]
    public async Task Reverse_settlement_posts_531_linked()
    {
        var (db, title) = await SeedAsync();
        var settlement = await Settle(db, title);

        await new FinancialDocumentsReverseSettlementService(db, Service(db)).ExecuteAsync(settlement.Key, "erro", "admin");

        var entries = await Entries(db);
        Assert.Equal(["530", "531"], entries.Select(e => e.StandardPostingCode));
        Assert.Equal(entries[0].DocEntry, entries[1].ReversalOfDocEntry);
        Assert.Equal(200m, entries[1].Lines.Single().Amount);
    }

    [Fact]
    public async Task Reverse_of_settlement_before_cutover_is_noop()
    {
        var (db, title) = await SeedAsync(cutover: false);
        var settlement = await Settle(db, title); // sem corte: baixa não contabiliza
        db.Context.AccountingSettings.Add(new AccountingSettings { CutoverDate = new DateTime(2026, 1, 1), UpdatedBy = "t" });
        await db.SaveChangesAsync();

        await new FinancialDocumentsReverseSettlementService(db, Service(db)).ExecuteAsync(settlement.Key, "erro", "admin");

        Assert.Empty(await Entries(db));
    }

    [Fact]
    public async Task Settlement_of_provisional_title_posts_nothing()
    {
        // O provisório não aceita baixa pela tela; linhas nele vêm de fluxos internos (aplicação de adiantamento).
        var (db, title) = await SeedAsync(FinancialDocumentNature.Provisional);
        db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = title.Key, SettlementDate = Day, Amount = 50m,
            Origin = FinancialSettlementOrigin.AdvanceApplication,
        });
        await SettlementPosting.EnqueuePendingAsync(Service(db), db.Context, "admin");
        await db.SaveChangesAsync();
        Assert.Empty(await Entries(db));
    }

    [Fact]
    public async Task Advance_refund_line_is_not_posted()
    {
        var (db, title) = await SeedAsync(FinancialDocumentNature.Advance);
        db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = title.Key, FinancialAccountCode = "BCO1", SettlementDate = Day,
            Amount = 50m, Origin = FinancialSettlementOrigin.AdvanceRefund,
        });
        await SettlementPosting.EnqueuePendingAsync(Service(db), db.Context, "admin");
        await db.SaveChangesAsync();
        Assert.Empty(await Entries(db));
    }

    [Fact]
    public async Task Calling_hook_twice_posts_once()
    {
        var (db, title) = await SeedAsync();
        db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = title.Key, FinancialAccountCode = "BCO1", SettlementDate = Day,
            Amount = 50m, Origin = FinancialSettlementOrigin.Manual,
        });
        await SettlementPosting.EnqueuePendingAsync(Service(db), db.Context, "admin");
        await SettlementPosting.EnqueuePendingAsync(Service(db), db.Context, "admin");
        await db.SaveChangesAsync();
        Assert.Single(await Entries(db));
    }

    [Fact]
    public void Every_service_that_adds_settlements_calls_hook()
    {
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "SiagroB1.Application"))) root = Path.GetDirectoryName(root)!;
        var offenders = Directory.GetFiles(Path.Combine(root, "SiagroB1.Application"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f) is var text && text.Contains("FinancialSettlements.Add") &&
                        !text.Contains("SettlementPosting.EnqueuePendingAsync") &&
                        !text.Contains("[settlement-posting: not applicable]"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }
}
