using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 (títulos firmes), regras 1–7: o firme nasce, muda e morre com o vínculo da conciliação.</summary>
public class FinancialDocumentsFirmSyncTests
{
    private const string Composed =
        "O título ST-0001 do vínculo já está composto no título FN-PROV-1: desfaça a composição antes de alterar a conciliação.";

    private static IQueryable<FinancialDocument> ActiveFirms(FirmPayableSeed seed) =>
        seed.Context.FinancialDocuments.AsNoTracking().Where(d =>
            d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation && d.Status != FinancialDocumentStatus.Canceled);

    [Fact]
    public async Task Firm_is_born_from_the_link_with_the_line_price_net_of_funrural()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync();

        var firm = Assert.IsType<FinancialDocument>(c.Firm);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, firm.NetAmount);
        Assert.Equal(FinancialDocumentNature.Firm, firm.Nature);
        Assert.Equal(FinancialDirection.Payable, firm.Direction);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        Assert.Equal(c.Link.Key, firm.OriginKey);
        Assert.Equal(FiscalReconciliationSeed.Supplier, firm.CardCode);
        Assert.Equal(seed.Contract.Key, firm.PurchaseContractKey);
        Assert.Equal("PC-1", firm.OriginDocNumber);
        Assert.Equal("01", firm.BranchCode);
        Assert.Equal(CurrencyType.Brl, firm.Currency);
        Assert.Equal(new DateTime(2026, 10, 9), firm.DocumentDate);
        Assert.Equal(new DateTime(2026, 10, 9), firm.DueDate);
        Assert.Equal(c.Invoice.Key, firm.PurchaseInvoiceKey);
        Assert.Equal(c.Transaction.Key, firm.StorageTransactionKey);
        Assert.Equal(c.Transaction.Code, firm.StorageTransactionCode);
        Assert.Equal("1001-1", firm.TaxDocumentNumber);
        Assert.Equal("ST-0001", firm.Code);
        Assert.Equal(new[] { TransactionCode.FinancialDocument }, seed.Numbers.RequestedCodes);
    }

    [Fact]
    public async Task Sapb1_creates_the_firm_at_gross_even_with_the_nature_flag_off()
    {
        // D10: em SAPB1 a natureza é o OUSG, sem a flag — conta como "gera". O Funrural da linha é zero: firme pelo bruto.
        var seed = await FirmPayableSeed.CreateAsync(erp: "SAPB1");

        var c = await seed.ConciliateAsync(funrural: 0m, createsFinancialDocument: false);

        Assert.Equal(20_000m, c.Firm!.NetAmount);
    }

    [Fact]
    public async Task Nature_without_the_creates_financial_flag_does_not_create_a_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync(createsFinancialDocument: false);

        Assert.Null(c.Firm);
        Assert.Empty(ActiveFirms(seed));
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task Line_without_nature_does_not_create_a_firm(string erp)
    {
        var seed = await FirmPayableSeed.CreateAsync(erp: erp);

        var c = await seed.ConciliateAsync(withUsage: false);

        Assert.Null(c.Firm);
    }

    [Fact]
    public async Task Nature_flag_turned_off_later_cancels_the_loose_firm_on_the_next_change()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var usage = await seed.Context.Usages.SingleAsync(u => u.Code == seed.FinancialUsageCode);
        usage.CreatesFinancialDocument = false;
        await seed.Context.SaveChangesAsync();

        await seed.SaveLinkAsync(c, 15_000m);

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(c.Firm!.Key)).Status);
    }

    [Fact]
    public async Task Line_without_contract_does_not_create_a_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync(lineWithContract: false);

        Assert.Null(c.Firm);
    }

    [Fact]
    public async Task Return_invoice_creates_a_purchase_return_title()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn);

        Assert.Equal(FinancialDocumentNature.PurchaseReturn, c.Firm!.Nature);
        Assert.Equal(2_959.25m, c.Firm.NetAmount); // 1.500 × 59.185,00 ÷ 30.000
    }

    [Fact]
    public async Task Saving_the_same_link_again_keeps_one_firm_and_no_log()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.SaveLinkAsync(c, 10_000m);

        Assert.Single(ActiveFirms(seed));
        Assert.Empty(seed.Context.FinancialDocumentChangeLogs.AsNoTracking());
    }

    [Fact]
    public async Task Changing_the_quantity_recalculates_the_loose_firm_and_logs_it()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.SaveLinkAsync(c, 15_000m);

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(29_592.50m, firm.NetAmount); // 15.000 × 59.185,00 ÷ 30.000
        var log = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, log.Field);
        Assert.Equal("19.728,33", log.OldValue);
        Assert.Equal("29.592,50", log.NewValue);
        Assert.Equal("bia", log.ChangedBy);
    }

    [Fact]
    public async Task Zero_quantity_cancels_the_loose_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.SaveLinkAsync(c, 0m);

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Vínculo fiscal desfeito", firm.CancellationReason);
        Assert.Equal("bia", firm.CanceledBy);
    }

    [Fact]
    public async Task Removing_the_link_cancels_the_loose_firm_and_relinking_creates_a_new_one()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.Reconciliation().RemoveAsync(c.Link.Key, "carla");

        var old = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, old.Status);
        Assert.Equal("carla", old.CanceledBy);

        await seed.SaveLinkAsync(c, 10_000m);

        var renewed = Assert.Single(ActiveFirms(seed));
        Assert.NotEqual(old.Key, renewed.Key);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, renewed.NetAmount);
    }

    [Fact]
    public async Task Composed_firm_refuses_quantity_change_and_removal()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        seed.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = seed.Provisional.Key, ComposedDocumentKey = c.Firm!.Key,
            Amount = c.Firm.NetAmount,
        });
        await seed.Context.SaveChangesAsync();

        var change = await Assert.ThrowsAsync<DefaultException>(() => seed.SaveLinkAsync(c, 15_000m));
        Assert.Equal(Composed, change.Message);

        seed.Context.ChangeTracker.Clear();
        var removal = await Assert.ThrowsAsync<DefaultException>(() => seed.Reconciliation().RemoveAsync(c.Link.Key, "carla"));
        Assert.Equal(Composed, removal.Message);

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(c.Firm.Key)).Status);
    }

    [Fact]
    public async Task Firm_with_dation_or_netting_counts_as_not_loose()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        seed.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = c.Firm!.Key, Amount = 100m, SettlementDate = DateTime.Today,
            Origin = FinancialSettlementOrigin.Dation, Notes = "sobra",
        });
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(seed.Context, c.Firm.Key);
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.SaveLinkAsync(c, 0m));

        Assert.Equal(
            "O título ST-0001 do vínculo já tem baixa por dação ou encontro de contas: estorne-a antes de alterar a conciliação.",
            error.Message);
    }

    [Fact]
    public async Task Cancelling_the_invoice_cancels_the_loose_firm_through_the_interceptor()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var c = await seed.ConciliateAsync();

        c.Invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Vínculo fiscal desfeito", firm.CancellationReason);
        Assert.Equal("Sistema", firm.CanceledBy);
    }

    [Fact]
    public async Task Cancelling_the_transaction_cancels_the_loose_firm_through_the_interceptor()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var c = await seed.ConciliateAsync();

        c.Transaction.TransactionStatus = StorageTransactionsStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(c.Firm!.Key)).Status);
        Assert.Empty(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Manual_cancel_of_a_conciliation_firm_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsCancelService(seed.Db).ExecuteAsync(c.Firm!.Key, "teste", "ana"));

        Assert.Equal(
            "Título firme nasce e morre com o vínculo da Conciliação Fiscal de Compras: desfaça o vínculo para cancelá-lo.",
            error.Message);
    }
    [Fact]
    public async Task Firm_is_born_in_brl_even_for_a_dollar_contract()
    {
        // D12: o firme nasce na moeda do documento de entrada (BRL), não na do contrato.
        var seed = await FirmPayableSeed.CreateAsync();
        seed.Contract.StandardCurrency = CurrencyType.Usd;
        await seed.Context.SaveChangesAsync();

        var c = await seed.ConciliateAsync();

        Assert.Equal(CurrencyType.Brl, c.Firm!.Currency);
    }
}
