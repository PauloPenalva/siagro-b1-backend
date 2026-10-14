using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 (títulos firmes), regras 9–14: composição manual no provisório e o desfazer.</summary>
public class FinancialDocumentsCompositionTests
{
    private static FinancialDocumentsComposeService Compose(FirmPayableSeed s) => new(s.Db, new FinancialDocumentChangeLogService(s.Context));
    private static FinancialDocumentsUncomposeService Uncompose(FirmPayableSeed s) => new(s.Db);
    private static FinancialDocumentsSettleService Settle(FirmPayableSeed s) => new(s.Db);

    private static Task SettleProvisionalAsync(FirmPayableSeed s, decimal amount) =>
        Settle(s).ExecuteAsync(s.Provisional.Key, "CX01", amount, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

    private static Task<Conciliation> ReturnAsync(FirmPayableSeed s, string taxNumber = "2001") =>
        s.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn, taxNumber: taxNumber); // 2.959,25, sem origem

    [Fact]
    public async Task Composing_a_firm_writes_the_row_the_offset_and_both_logs()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        Assert.Equal(seed.Provisional.Key, row.ProvisionalDocumentKey);
        Assert.Equal(c.Firm.Key, row.ComposedDocumentKey);
        Assert.Equal(19_728.33m, row.Amount);

        var offset = await seed.Context.FinancialSettlements.AsNoTracking().SingleAsync(s => s.FinancialDocumentKey == c.Firm.Key);
        Assert.Equal(FinancialSettlementOrigin.InvoiceOffset, offset.Origin);
        Assert.Equal(19_728.33m, offset.Amount);
        Assert.Null(offset.FinancialAccountCode);
        Assert.Equal(seed.Provisional.Key, offset.RelatedDocumentKey);
        Assert.Equal(row.Key, offset.CompositionKey);
        Assert.Equal("FN-PROV-1", offset.DocumentReference);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(19_728.33m, provisional.ComposedAmount);
        Assert.Equal(19_728.33m, provisional.AvailableToSettleAmount);
        Assert.Equal(0m, provisional.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, provisional.Status);

        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(0m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, firm.Status);

        var logs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().ToListAsync();
        Assert.All(logs, l => Assert.Equal(FinancialDocumentChangeLogFields.Composition, l.Field));
        Assert.Equal("ST-0001: 19.728,33", logs.Single(l => l.FinancialDocumentKey == seed.Provisional.Key).NewValue);
        Assert.Equal("FN-PROV-1: 19.728,33", logs.Single(l => l.FinancialDocumentKey == c.Firm.Key).NewValue);
        Assert.All(logs, l => Assert.Null(l.OldValue));
    }

    [Fact]
    public async Task Firm_that_does_not_fit_enters_partially()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m);
        var c = await seed.ConciliateAsync();

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        Assert.Equal(15_000m, (await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync()).Amount);
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(4_728.33m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, firm.Status);
        Assert.Equal(15_000m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);
    }

    [Fact]
    public async Task Listed_order_decides_who_enters_first()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 25_000m);
        var c1 = await seed.ConciliateAsync(taxNumber: "1001");
        var c2 = await seed.ConciliateAsync(taxNumber: "1002");

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c2.Firm!.Key, c1.Firm!.Key], "ana");

        var rows = await seed.Context.FinancialDocumentCompositions.AsNoTracking().ToListAsync();
        Assert.Equal(19_728.33m, rows.Single(r => r.ComposedDocumentKey == c2.Firm.Key).Amount);
        Assert.Equal(5_271.67m, rows.Single(r => r.ComposedDocumentKey == c1.Firm.Key).Amount);
    }

    [Fact]
    public async Task Fully_composed_provisional_refuses_another_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: FirmPayableSeed.DefaultFirmAmount);
        var c1 = await seed.ConciliateAsync(taxNumber: "1001");
        var c2 = await seed.ConciliateAsync(taxNumber: "1002");
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c1.Firm!.Key], "ana");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(seed).ExecuteAsync(seed.Provisional.Key, [c2.Firm!.Key], "ana"));

        Assert.Equal("O título FN-PROV-1 já está integralmente composto.", error.Message);
        seed.Context.ChangeTracker.Clear();
        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
    }

    [Fact]
    public async Task Repeated_key_is_composed_once()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key, c.Firm.Key], "ana");

        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
    }

    [Fact]
    public async Task Return_enters_negative_and_frees_space()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var r = await ReturnAsync(seed);

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key, r.Firm!.Key], "ana");

        Assert.Equal(-2_959.25m, (await seed.Context.FinancialDocumentCompositions.AsNoTracking()
            .SingleAsync(x => x.ComposedDocumentKey == r.Firm.Key)).Amount);
        Assert.Equal(16_769.08m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);
        var refund = await seed.ReloadAsync(r.Firm.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, refund.Status);
        Assert.Equal(2_959.25m, (await seed.Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.FinancialDocumentKey == r.Firm.Key)).Amount);
    }

    [Fact]
    public async Task Return_cannot_take_the_composed_below_the_settled()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        await SettleProvisionalAsync(seed, 19_000m);
        var r = await ReturnAsync(seed);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(seed).ExecuteAsync(seed.Provisional.Key, [r.Firm!.Key], "ana"));

        Assert.Equal(
            "A devolução ST-0002 deixaria o composto do título FN-PROV-1 abaixo do valor já baixado: estorne a baixa antes.",
            error.Message);
    }

    [Fact]
    public async Task Firm_of_another_contract_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var other = await seed.Fiscal.AddContractAsync("PC-2");
        var c = await seed.ConciliateAsync(contract: other);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana"));

        Assert.Equal("O título ST-0001 é de outro contrato.", error.Message);
    }

    [Fact]
    public async Task Uncompose_mirrors_the_offset_and_frees_the_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        var offset = await seed.Context.FinancialSettlements.AsNoTracking().SingleAsync();

        await Uncompose(seed).ExecuteAsync(row.Key, "bia");

        Assert.Empty(seed.Context.FinancialDocumentCompositions.AsNoTracking());
        var reversal = await seed.Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.Origin == FinancialSettlementOrigin.Reversal);
        Assert.Equal(c.Firm.Key, reversal.FinancialDocumentKey);
        Assert.Equal(-19_728.33m, reversal.Amount);
        Assert.Equal(offset.Key, reversal.ReversedSettlementKey);
        Assert.Equal("Composição desfeita", reversal.Notes);

        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        Assert.Equal(0m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);

        var undoLogs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().Where(l => l.NewValue == null).ToListAsync();
        Assert.Equal(2, undoLogs.Count);
        Assert.Contains(undoLogs, l => l.FinancialDocumentKey == seed.Provisional.Key && l.OldValue == "ST-0001: 19.728,33");
    }

    [Fact]
    public async Task Uncompose_is_refused_while_the_settlement_needs_that_composition()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        await SettleProvisionalAsync(seed, 10_000m);
        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Uncompose(seed).ExecuteAsync(row.Key, "bia"));

        Assert.Equal("Estorne a baixa do título FN-PROV-1 antes de desfazer a composição.", error.Message);
    }

    [Fact]
    public async Task Generic_reversal_of_an_offset_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        var offset = await seed.Context.FinancialSettlements.AsNoTracking().SingleAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsReverseSettlementService(seed.Db).ExecuteAsync(offset.Key, "errado", "ana"));

        Assert.Equal("Abatimento de composição se desfaz pela composição.", error.Message);
    }

    [Fact]
    public async Task Resaving_a_composed_link_with_same_quantity_is_accepted()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        await seed.SaveLinkAsync(c, 10_000m);

        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(c.Firm.Key)).Status);
    }

    [Fact]
    public async Task Cancelling_the_transaction_of_a_composed_firm_is_refused_by_the_interceptor()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        c.Transaction.TransactionStatus = StorageTransactionsStatus.Cancelled;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(
            "O título ST-0001 do vínculo já está composto no título FN-PROV-1: desfaça a composição antes de alterar a conciliação.",
            error.Message);
    }

    [Fact]
    public async Task Invoice_cancel_dry_run_refuses_a_composed_firm_before_sefaz()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            new SiagroB1.Application.Services.PurchaseInvoices.PurchaseInvoicesCancelService(seed.Db).EnsureCanCancelAsync(c.Invoice.Key));

        Assert.Equal(
            "O título ST-0001 do vínculo já está composto no título FN-PROV-1: desfaça a composição antes de alterar a conciliação.",
            error.Message);
    }

    [Fact]
    public async Task Invoice_cancel_dry_run_passes_with_a_loose_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await new SiagroB1.Application.Services.PurchaseInvoices.PurchaseInvoicesCancelService(seed.Db).EnsureCanCancelAsync(c.Invoice.Key);
    }
}
