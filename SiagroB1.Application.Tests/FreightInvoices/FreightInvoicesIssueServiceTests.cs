using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.FreightInvoices;

public class FreightInvoicesIssueServiceTests
{
    private const string Branch = "01";
    private static readonly DateTime Issue = new(2026, 10, 22, 10, 0, 0);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FreightInvoicesIssueService Svc(string erp = "STANDALONE") =>
        new(_db, new FakeDocNumberSequenceService(), new FreightInvoiceEligibleCtesQuery(_db), TestConfiguration.Erp(erp));

    private async Task<TransportDocument> CteAsync(int number, decimal amount = 1000m)
    {
        var cte = new TransportDocument
        {
            BranchCode = Branch, Series = 1, Number = number, AccessKey = new string('1', 44), Status = CteStatus.Authorized,
            IssuedAt = Issue, TakerRole = 3,
            Parties = [new TransportDocumentParty { Role = CtePartyRole.Recipient, CardCode = "DES", Name = "DES" }],
        };
        _db.Context.TransportDocuments.Add(cte);
        _db.Context.FinancialDocuments.Add(new FinancialDocument
        {
            Code = "CTE-" + number, DocNumberKey = Guid.NewGuid(), BranchCode = Branch,
            Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open, CardCode = "DES", DocumentDate = Issue, DueDate = Issue.AddDays(30),
            NetAmount = amount, InstallmentNumber = 1, CostCenterCode = "CC1",
            OriginType = FinancialDocumentOrigin.TransportDocument, OriginKey = cte.Key, CreatedBy = "t", UpdatedBy = "t",
        });
        await _db.SaveChangesAsync();
        return cte;
    }

    private async Task<FreightInvoice> DraftAsync(int? condition = null, params TransportDocument[] ctes)
    {
        var save = new FreightInvoicesSaveService(_db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["DES"] = "Destinatário" }),
            new FreightInvoiceEligibleCtesQuery(_db), TestConfiguration.Erp("STANDALONE"));
        return await save.SaveAsync(new FreightInvoiceSaveModel(null, Branch, "DES", Issue, Issue.AddDays(15), condition, null,
            ctes.Select(c => c.Key).ToList(), null, null), "u");
    }

    private Task<List<FinancialDocument>> InvoiceTitles(Guid key) =>
        _db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.FreightInvoice && d.OriginKey == key).ToListAsync();

    [Fact]
    public async Task Issue_SettlesCteTitles_ByAggregation_NoAccount()
    {
        var a = await CteAsync(1, 1000m);
        var b = await CteAsync(2, 250.50m);
        var inv = await DraftAsync(null, a, b);

        await Svc().IssueAsync(inv.Key, "u");

        var cteTitles = await _db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.TransportDocument).ToListAsync();
        Assert.All(cteTitles, t => Assert.Equal(0m, t.OpenAmount));
        var settlements = await _db.Context.FinancialSettlements.AsNoTracking().ToListAsync();
        Assert.Equal(2, settlements.Count);
        Assert.All(settlements, s =>
        {
            Assert.Equal(FinancialSettlementOrigin.Aggregation, s.Origin);
            Assert.Null(s.FinancialAccountCode);
            Assert.Equal(inv.Key, s.RelatedDocumentKey);
            Assert.Equal(inv.Code, s.DocumentReference);
            Assert.Equal(Issue.Date, s.SettlementDate);
        });
        var issued = await _db.Context.FreightInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(FreightInvoiceStatus.Issued, issued.Status);
        Assert.NotNull(issued.IssuedAt);
    }

    [Fact]
    public async Task Issue_CreatesSingleReceivable_WithTotal()
    {
        var a = await CteAsync(1, 1000m);
        var b = await CteAsync(2, 250.50m);
        var inv = await DraftAsync(null, a, b);

        await Svc().IssueAsync(inv.Key, "u");

        var title = Assert.Single(await InvoiceTitles(inv.Key));
        Assert.Equal(1250.50m, title.NetAmount);
        Assert.Equal(1250.50m, title.OpenAmount);
        Assert.Equal(FinancialDirection.Receivable, title.Direction);
        Assert.Equal("DES", title.CardCode);
        Assert.Equal(inv.Code, title.OriginDocNumber);
        Assert.Equal(Issue.AddDays(15).Date, title.DueDate);
        Assert.Equal("CC1", title.CostCenterCode);
    }

    [Fact]
    public async Task Issue_WithPaymentCondition_SplitsInstallments()
    {
        var condition = new PaymentCondition { Name = "30/60", Days = "30,60", PaymentMeans = "15" };
        _db.Context.PaymentConditions.Add(condition);
        await _db.SaveChangesAsync();
        var a = await CteAsync(1, 1000.01m);
        var inv = await DraftAsync(condition.Code, a);

        await Svc().IssueAsync(inv.Key, "u");

        var titles = await InvoiceTitles(inv.Key);
        Assert.Equal(2, titles.Count);
        Assert.Equal(1000.01m, titles.Sum(t => t.NetAmount));
        Assert.Equal(new[] { 1, 2 }, titles.OrderBy(t => t.InstallmentNumber).Select(t => t.InstallmentNumber ?? 0));
    }

    [Fact]
    public async Task Issue_DoesNotEnqueueAccountingPosting()
    {
        Assert.False(SettlementPosting.IsPostable(
            new FinancialSettlement { Origin = FinancialSettlementOrigin.Aggregation, Amount = 10m },
            new FinancialDocument { Code = "x", CardCode = "x", Nature = FinancialDocumentNature.Firm }));
        Assert.False(FinancialDocumentPosting.IsPostable(
            new FinancialDocument { Code = "x", CardCode = "x", Nature = FinancialDocumentNature.Firm, OriginType = FinancialDocumentOrigin.FreightInvoice }));

        // Backstop: mesmo se alguém enfileirar com o motor, a baixa Aggregation é ignorada.
        var engine = new RecordingEngine();
        var a = await CteAsync(1);
        var title = await _db.Context.FinancialDocuments.FirstAsync();
        _db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            FinancialDocumentKey = title.Key, Amount = 10m, Origin = FinancialSettlementOrigin.Aggregation,
            SettlementDate = Issue, CreatedBy = "t", UpdatedBy = "t",
        });
        await SettlementPosting.EnqueuePendingAsync(engine, _db.Context, "u");
        Assert.Equal(0, engine.Calls);
        _db.Context.ChangeTracker.Clear();

        var inv = await DraftAsync(null, a);
        await Svc().IssueAsync(inv.Key, "u");
        Assert.Empty(await _db.Context.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task Issue_NoAccountMovement()
    {
        var a = await CteAsync(1);
        var inv = await DraftAsync(null, a);
        await Svc().IssueAsync(inv.Key, "u");

        Assert.True(await _db.Context.FinancialSettlements.AllAsync(s => s.FinancialAccountCode == null));
        Assert.Empty(await FinancialAccountMovementsQuery.Query(_db.Context, "BCO1").ToListAsync());
    }

    [Fact]
    public async Task Issue_CteSettledMeanwhile_RejectedAndRollsBack()
    {
        var a = await CteAsync(1);
        var b = await CteAsync(7);
        var inv = await DraftAsync(null, a, b);
        var titleB = await _db.Context.FinancialDocuments.FirstAsync(d => d.OriginKey == b.Key);
        _db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            FinancialDocumentKey = titleB.Key, Amount = 10m, Origin = FinancialSettlementOrigin.Manual, FinancialAccountCode = "BCO1",
            SettlementDate = Issue, CreatedBy = "t", UpdatedBy = "t",
        });
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, titleB.Key);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc().IssueAsync(inv.Key, "u"));
        Assert.StartsWith("CT-e 7 não pode ser faturado: ", ex.Message);

        Assert.Empty(await InvoiceTitles(inv.Key));
        Assert.False(await _db.Context.FinancialSettlements.AnyAsync(s => s.Origin == FinancialSettlementOrigin.Aggregation));
        Assert.Equal(FreightInvoiceStatus.Draft, (await _db.Context.FreightInvoices.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ReverseSettlement_Aggregation_Rejected()
    {
        var a = await CteAsync(1);
        var inv = await DraftAsync(null, a);
        await Svc().IssueAsync(inv.Key, "u");
        var s = await _db.Context.FinancialSettlements.AsNoTracking().SingleAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsReverseSettlementService(_db, null).ExecuteAsync(s.Key, "erro", "u"));
        Assert.Equal("Baixa por fatura de frete: cancele a fatura para desfazer.", ex.Message);
    }

    [Fact]
    public async Task CancelInvoiceTitle_FromFinancial_Rejected()
    {
        var a = await CteAsync(1);
        var inv = await DraftAsync(null, a);
        await Svc().IssueAsync(inv.Key, "u");
        var title = Assert.Single(await InvoiceTitles(inv.Key));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsCancelService(_db).ExecuteAsync(title.Key, "motivo", "u"));
        Assert.Equal("Título da fatura de frete: cancele pela fatura.", ex.Message);
    }

    [Fact]
    public async Task Issue_NotDraft_Rejected()
    {
        var a = await CteAsync(1);
        var inv = await DraftAsync(null, a);
        await Svc().IssueAsync(inv.Key, "u");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc().IssueAsync(inv.Key, "u"));
        Assert.Equal("Só o rascunho da fatura pode ser emitido.", ex.Message);
        Assert.Single(await InvoiceTitles(inv.Key));
        Assert.Single(await _db.Context.FinancialSettlements.ToListAsync());
    }

    [Fact]
    public async Task Issue_NotStandalone_Rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc("SAPB1").IssueAsync(Guid.NewGuid(), "u"));
        Assert.Equal(SiagroB1.Application.Services.Cte.BranchCteSettingsService.StandaloneOnly, ex.Message);
    }

    private sealed class RecordingEngine : IAccountingPostingService
    {
        public int Calls { get; private set; }
        public Task<JournalEntry?> PostAsync(string code, PostingContext context, string user, CancellationToken ct = default)
        { Calls++; return Task.FromResult<JournalEntry?>(null); }
        public Task<JournalEntry?> PostReversalAsync(string code, PostingContext context, string originalEvent, string user,
            CancellationToken ct = default)
        { Calls++; return Task.FromResult<JournalEntry?>(null); }
    }
}
