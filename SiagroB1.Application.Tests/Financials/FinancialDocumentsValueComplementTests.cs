using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 D11: o firme do complemento de valor nasce na confirmação, um por linha, e morre com o documento.</summary>
public class FinancialDocumentsValueComplementTests
{
    private static async Task<PurchaseInvoice> ConfirmedComplementAsync(FirmPayableSeed seed, params decimal[] amounts)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier,
            InvoiceType = PurchaseInvoiceType.ValueComplement, IssuerType = DocumentIssuerType.ThirdParty,
            TaxDocumentNumber = "5001", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 12),
        };
        foreach (var amount in amounts)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
                ComplementAmount = amount, UsageCode = seed.FinancialUsageCode, PurchaseContractKey = seed.Contract.Key,
            });

        await new PurchaseInvoicesCreateService(seed.Db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.PurchaseApply(seed.Db, new FakeBusinessPartnerService(), seed.Erp), new FakeDocNumberSequenceService())
            .ExecuteAsync(invoice, "ana");
        await TaxTestServices.PurchaseConfirm(seed.Db, seed.Erp, firmNumbers: seed.Numbers).ExecuteAsync(invoice.Key, "ana");
        return invoice;
    }

    private static IQueryable<FinancialDocument> ComplementFirms(FirmPayableSeed seed) =>
        seed.Context.FinancialDocuments.AsNoTracking().Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseInvoice);

    [Fact]
    public async Task Confirming_creates_one_firm_per_line()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var invoice = await ConfirmedComplementAsync(seed, 1_000m, 500m);

        var firms = await ComplementFirms(seed).OrderByDescending(d => d.NetAmount).ToListAsync();
        Assert.Equal(new[] { 1_000m, 500m }, firms.Select(f => f.NetAmount));
        var lineKeys = invoice.Items.Select(i => i.Key!.Value).ToHashSet();
        Assert.All(firms, f =>
        {
            Assert.Equal(FinancialDocumentNature.Firm, f.Nature);
            Assert.Contains(f.OriginKey!.Value, lineKeys);
            Assert.Equal(invoice.Key, f.PurchaseInvoiceKey);
            Assert.Null(f.StorageTransactionKey);
            Assert.Equal("5001-1", f.TaxDocumentNumber);
            Assert.Equal(seed.Contract.Key, f.PurchaseContractKey);
            Assert.Equal("PC-1", f.OriginDocNumber);
            Assert.Equal(new DateTime(2026, 10, 12), f.DueDate);
            Assert.True(f.IsPurchaseFirmPayable);
        });
    }

    [Fact]
    public async Task Underbilling_is_closed_by_composing_the_complement()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 20_000m);
        var c = await seed.ConciliateAsync();                         // 19.728,33
        await ConfirmedComplementAsync(seed, 271.67m);
        var complement = await ComplementFirms(seed).SingleAsync();

        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key, complement.Key], "ana");
        await new FinancialDocumentsSettleService(seed.Db)
            .ExecuteAsync(seed.Provisional.Key, "CX01", 20_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(seed.Provisional.Key)).Status);
    }

    [Fact]
    public async Task Cancelling_the_document_cancels_the_loose_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);

        await new PurchaseInvoicesCancelService(seed.Db).ExecuteAsync(invoice.Key, "bia");

        var firm = await ComplementFirms(seed).SingleAsync();
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Documento de complemento de valor cancelado ou estornado", firm.CancellationReason);
        Assert.Equal("bia", firm.CanceledBy);
    }

    [Fact]
    public async Task Cancelling_the_document_with_a_composed_firm_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);
        var firm = await ComplementFirms(seed).SingleAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [firm.Key], "ana");

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(seed.Db).ExecuteAsync(invoice.Key, "bia"));

        Assert.Equal(
            $"O título {firm.Code} do complemento de valor já está composto no título FN-PROV-1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Pre_sefaz_dry_run_refuses_a_complement_with_a_composed_firm()
    {
        // Sem o interceptor: só o ensaio (EnsureCanCancelAsync, antes de falar com a SEFAZ) pode recusar.
        var seed = await FirmPayableSeed.CreateAsync();
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);
        var firm = await ComplementFirms(seed).SingleAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [firm.Key], "ana");

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(seed.Db).EnsureCanCancelAsync(invoice.Key));

        Assert.Equal(
            $"O título {firm.Code} do complemento de valor já está composto no título FN-PROV-1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Pre_sefaz_dry_run_accepts_a_complement_with_a_loose_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);

        await new PurchaseInvoicesCancelService(seed.Db).EnsureCanCancelAsync(invoice.Key);
    }

    [Fact]
    public async Task Manual_cancel_of_a_complement_firm_points_to_the_entry_document()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await ConfirmedComplementAsync(seed, 1_000m);
        var firm = await ComplementFirms(seed).SingleAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            new FinancialDocumentsCancelService(seed.Db).ExecuteAsync(firm.Key, "teste", "ana"));

        Assert.Equal(
            "Título firme do complemento de valor nasce da confirmação do documento de entrada: cancele ou estorne o documento para cancelá-lo.",
            error.Message);
    }

    [Fact]
    public async Task Reversing_the_confirmation_cancels_the_loose_firm_and_reconfirming_creates_a_new_one()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);
        var first = await ComplementFirms(seed).SingleAsync();

        await new PurchaseInvoicesReverseConfirmService(seed.Db).ExecuteAsync(invoice.Key, "bia");
        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(first.Key)).Status);

        await TaxTestServices.PurchaseConfirm(seed.Db, seed.Erp, firmNumbers: seed.Numbers).ExecuteAsync(invoice.Key, "ana");

        var active = await ComplementFirms(seed).Where(d => d.Status != FinancialDocumentStatus.Canceled).SingleAsync();
        Assert.NotEqual(first.Key, active.Key);
    }

    [Fact]
    public async Task Value_complement_is_not_conciliated()
    {
        var fiscal = await FiscalReconciliationSeed.CreateAsync();
        var tx = await fiscal.AddTransactionAsync(10_000m);
        var (_, line) = await fiscal.AddInvoiceAsync(0m, type: PurchaseInvoiceType.ValueComplement, taxNumber: "5001");

        var error = await Assert.ThrowsAsync<DefaultException>(() => FiscalReconciliationSeed.NewService(fiscal.Db).SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = tx.Key, Quantity = 10m }], "ana"));

        Assert.Equal("O documento 5001-1 é complemento de valor: não se concilia com romaneio; o título nasce na confirmação.", error.Message);
        Assert.Empty(await FiscalReconciliationSeed.NewService(fiscal.Db).ListInvoiceLinesAsync(
            new FiscalReconciliationFilter { CardCode = FiscalReconciliationSeed.Supplier, ItemCode = FiscalReconciliationSeed.Product }));
    }
    [Fact]
    public async Task Complement_firm_is_born_in_brl_even_for_a_dollar_contract()
    {
        // D12.
        var seed = await FirmPayableSeed.CreateAsync();
        seed.Contract.StandardCurrency = CurrencyType.Usd;
        await seed.Context.SaveChangesAsync();

        await ConfirmedComplementAsync(seed, 1_000m);

        Assert.Equal(CurrencyType.Brl, (await ComplementFirms(seed).SingleAsync()).Currency);
    }
}
