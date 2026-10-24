using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FreightInvoices;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FreightInvoices;

public class FreightInvoicesCancelServiceTests
{
    private const string Branch = "01";
    private static readonly DateTime Issue = new(2026, 10, 22, 10, 0, 0);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FreightInvoicesCancelService Cancel(string erp = "STANDALONE") => new(_db, TestConfiguration.Erp(erp));

    private FreightInvoiceEligibleCtesQuery Eligible() => new(_db);

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

    private async Task<FreightInvoice> DraftAsync(params TransportDocument[] ctes)
    {
        var save = new FreightInvoicesSaveService(_db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["DES"] = "Destinatário" }),
            Eligible(), TestConfiguration.Erp("STANDALONE"));
        return await save.SaveAsync(new FreightInvoiceSaveModel(null, Branch, "DES", Issue, Issue.AddDays(15), null, null,
            ctes.Select(c => c.Key).ToList(), null, null), "u");
    }

    private async Task<FreightInvoice> IssuedAsync(params TransportDocument[] ctes)
    {
        var inv = await DraftAsync(ctes);
        await new FreightInvoicesIssueService(_db, new FakeDocNumberSequenceService(), Eligible(), TestConfiguration.Erp("STANDALONE"))
            .IssueAsync(inv.Key, "u");
        _db.Context.ChangeTracker.Clear();
        return inv;
    }

    private Task<FreightInvoice> Reload(Guid key) =>
        _db.Context.FreightInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == key);

    private Task<FinancialDocument> InvoiceTitle(Guid key) =>
        _db.Context.FinancialDocuments.AsNoTracking()
            .SingleAsync(d => d.OriginType == FinancialDocumentOrigin.FreightInvoice && d.OriginKey == key);

    /// <summary>Baixa manual no título da fatura, pelo caminho do ledger (recálculo + save).</summary>
    private async Task<FinancialSettlement> SettleInvoiceTitleAsync(Guid invoiceKey, decimal amount)
    {
        var title = await InvoiceTitle(invoiceKey);
        var s = new FinancialSettlement
        {
            FinancialDocumentKey = title.Key, Amount = amount, Origin = FinancialSettlementOrigin.Manual,
            FinancialAccountCode = "BCO1", SettlementDate = Issue, CreatedBy = "t", UpdatedBy = "t",
        };
        _db.Context.FinancialSettlements.Add(s);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, title.Key);
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
        return s;
    }

    [Fact]
    public async Task CancelDraft_FreesCtes()
    {
        var a = await CteAsync(1);
        var inv = await DraftAsync(a);
        Assert.Empty(await Eligible().ListAsync(Branch, "DES", null, null, null));

        await Cancel().CancelAsync(inv.Key, "u");

        var reloaded = await Reload(inv.Key);
        Assert.Equal(FreightInvoiceStatus.Cancelled, reloaded.Status);
        Assert.All(reloaded.Items, i => Assert.False(i.IsActive));
        Assert.Single(await Eligible().ListAsync(Branch, "DES", null, null, null));
    }

    [Fact]
    public async Task CancelIssued_ReopensCteTitles()
    {
        var a = await CteAsync(1, 1000m);
        var b = await CteAsync(2, 250.50m);
        var inv = await IssuedAsync(a, b);

        await Cancel().CancelAsync(inv.Key, "u");

        var cteTitles = await _db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.TransportDocument).ToListAsync();
        Assert.Equal(1000m, cteTitles.Single(t => t.OriginKey == a.Key).OpenAmount);
        Assert.Equal(250.50m, cteTitles.Single(t => t.OriginKey == b.Key).OpenAmount);
        Assert.All(cteTitles, t => Assert.Equal(FinancialDocumentStatus.Open, t.Status));

        var settlements = await _db.Context.FinancialSettlements.AsNoTracking().ToListAsync();
        var aggregations = settlements.Where(s => s.Origin == FinancialSettlementOrigin.Aggregation).ToList();
        var reversals = settlements.Where(s => s.Origin == FinancialSettlementOrigin.Reversal).ToList();
        Assert.Equal(2, reversals.Count);
        Assert.All(aggregations, s => Assert.Contains(reversals, r => r.ReversedSettlementKey == s.Key && r.Amount == -s.Amount));
        Assert.All(reversals, r => Assert.Null(r.FinancialAccountCode));

        var title = await InvoiceTitle(inv.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, title.Status);
        Assert.NotNull(title.CanceledAt);
        Assert.False(string.IsNullOrEmpty(title.CancellationReason));

        var reloaded = await Reload(inv.Key);
        Assert.Equal(FreightInvoiceStatus.Cancelled, reloaded.Status);
        Assert.All(reloaded.Items, i => Assert.False(i.IsActive));

        Assert.Equal(2, (await Eligible().ListAsync(Branch, "DES", null, null, null)).Count);
        Assert.Empty(await _db.Context.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task CancelIssued_WithSettlement_Rejected()
    {
        var a = await CteAsync(1);
        var inv = await IssuedAsync(a);
        await SettleInvoiceTitleAsync(inv.Key, 100m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(inv.Key, "u"));
        Assert.Equal("A fatura possui baixa; estorne a baixa antes de cancelar.", ex.Message);

        Assert.False(await _db.Context.FinancialSettlements.AnyAsync(s => s.Origin == FinancialSettlementOrigin.Reversal));
        Assert.Equal(FreightInvoiceStatus.Issued, (await Reload(inv.Key)).Status);
    }

    [Fact]
    public async Task CancelTwice_SecondRejected()
    {
        var a = await CteAsync(1);
        var inv = await IssuedAsync(a);
        await Cancel().CancelAsync(inv.Key, "u");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(inv.Key, "u"));
        Assert.Equal("A fatura já está cancelada.", ex.Message);
        Assert.Equal(1, await _db.Context.FinancialSettlements.CountAsync(s => s.Origin == FinancialSettlementOrigin.Reversal));
    }

    [Fact]
    public async Task Cancel_NotStandalone_Rejected()
    {
        var a = await CteAsync(1);
        var inv = await DraftAsync(a);
        await Assert.ThrowsAsync<DefaultException>(() => Cancel("SAPB1").CancelAsync(inv.Key, "u"));
    }

    [Fact]
    public async Task FullSettlement_MarksSettled()
    {
        var a = await CteAsync(1, 1000m);
        var inv = await IssuedAsync(a);

        await SettleInvoiceTitleAsync(inv.Key, 400m);
        Assert.Equal(FreightInvoiceStatus.Issued, (await Reload(inv.Key)).Status);

        await SettleInvoiceTitleAsync(inv.Key, 600m);
        Assert.Equal(FreightInvoiceStatus.Settled, (await Reload(inv.Key)).Status);
    }

    [Fact]
    public async Task ReverseSettlement_BackToIssued()
    {
        var a = await CteAsync(1, 1000m);
        var inv = await IssuedAsync(a);
        var s = await SettleInvoiceTitleAsync(inv.Key, 1000m);
        Assert.Equal(FreightInvoiceStatus.Settled, (await Reload(inv.Key)).Status);

        await new FinancialDocumentsReverseSettlementService(_db, null).ExecuteAsync(s.Key, "erro", "u");

        Assert.Equal(FreightInvoiceStatus.Issued, (await Reload(inv.Key)).Status);
    }
}
