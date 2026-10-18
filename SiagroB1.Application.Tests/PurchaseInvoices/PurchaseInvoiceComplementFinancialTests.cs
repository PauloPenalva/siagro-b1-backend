using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Financials;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Spec 2026-10-17 (complementos), financeiro e conciliação: o complemento de valor sem contrato (natureza de outra compra)
/// gera as parcelas de outras compras; com contrato segue gerando o firme; o complemento de ICMS nunca gera título e não
/// se concilia com romaneio.
/// </summary>
public class PurchaseInvoiceComplementFinancialTests
{
    private sealed record Scenario(
        FiscalReconciliationSeed Fiscal, FakeDocNumberSequenceService Numbers, PurchaseContract Contract,
        int ContractUsage, int OtherPurchase, int NonFinancial)
    {
        public UnitOfWork Db => Fiscal.Db;
    }

    private static async Task<Scenario> SeedAsync()
    {
        var fiscal = await FiscalReconciliationSeed.CreateAsync();
        var contract = await fiscal.AddContractAsync();
        var usages = new UsageService(fiscal.Db, NullLogger<UsageService>.Instance);
        var contractUsage = (await usages.CreateAsync(Usage("Compra de grãos", true, true))).Code;
        var otherPurchase = (await usages.CreateAsync(Usage("Serviço tomado", false, true))).Code;
        var nonFinancial = (await usages.CreateAsync(Usage("Complemento sem financeiro", false, false))).Code;
        return new Scenario(fiscal, new FakeDocNumberSequenceService(), contract, contractUsage, otherPurchase, nonFinancial);
    }

    private static UsageModel Usage(string name, bool requiresContract, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        RequiresContract = requiresContract, CreatesFinancialDocument = createsFinancial,
    };

    private static async Task<PurchaseInvoice> CreateAsync(
        Scenario s, PurchaseInvoiceType type, int usageCode, Guid? contractKey, int? paymentCondition = null)
    {
        var isIcms = type == PurchaseInvoiceType.IcmsComplement;
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier, InvoiceType = type,
            IssuerType = DocumentIssuerType.ThirdParty, TaxDocumentNumber = "7001", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 17), PaymentConditionCode = paymentCondition,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            ComplementAmount = isIcms ? 0m : 1_500m, IcmsBase = isIcms ? 1_000m : 0m, IcmsValue = isIcms ? 120m : 0m,
            UsageCode = usageCode, PurchaseContractKey = contractKey,
        });
        var partners = new FakeBusinessPartnerService();
        await new PurchaseInvoicesCreateService(s.Db, partners, new FakeItemService(),
                TaxTestServices.PurchaseApply(s.Db, partners, "STANDALONE"), new FakeDocNumberSequenceService())
            .ExecuteAsync(invoice, "ana");
        return invoice;
    }

    private static Task ConfirmAsync(Scenario s, PurchaseInvoice invoice) =>
        TaxTestServices.PurchaseConfirm(s.Db, "STANDALONE", firmNumbers: s.Numbers).ExecuteAsync(invoice.Key, "ana");

    private static Task<List<FinancialDocument>> TitlesAsync(Scenario s, PurchaseInvoice invoice) =>
        s.Db.Context.FinancialDocuments.AsNoTracking().Where(d => d.PurchaseInvoiceKey == invoice.Key)
            .OrderBy(d => d.InstallmentNumber).ToListAsync();

    [Fact]
    public async Task Value_complement_of_other_purchase_creates_installments_on_confirm()
    {
        var s = await SeedAsync();
        var condition = new PaymentCondition { Name = "30,60", Days = "30,60", PaymentMeans = "15" };
        s.Db.Context.PaymentConditions.Add(condition);
        await s.Db.Context.SaveChangesAsync();
        var invoice = await CreateAsync(s, PurchaseInvoiceType.ValueComplement, s.OtherPurchase, null, condition.Code);

        await ConfirmAsync(s, invoice);

        var titles = await TitlesAsync(s, invoice);
        Assert.Equal(2, titles.Count);
        Assert.All(titles, t => Assert.Equal(FinancialDocumentOrigin.OtherPurchaseInvoice, t.OriginType));
        Assert.Equal(1_500m, titles.Sum(t => t.NetAmount));
        Assert.Empty(s.Db.Context.FinancialDocuments.AsNoTracking().Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseInvoice));
        Assert.Equal(InvoiceStatus.Confirmed,
            (await s.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Value_complement_of_other_purchase_titles_are_cancelled_on_reverse()
    {
        var s = await SeedAsync();
        var invoice = await CreateAsync(s, PurchaseInvoiceType.ValueComplement, s.OtherPurchase, null);
        await ConfirmAsync(s, invoice);
        Assert.NotEmpty(await TitlesAsync(s, invoice));

        await FinancialDocumentTestServices.PurchaseReverse(s.Db).ExecuteAsync(invoice.Key, "bia");

        Assert.All(await TitlesAsync(s, invoice), t => Assert.Equal(FinancialDocumentStatus.Canceled, t.Status));
    }

    [Fact]
    public async Task Value_complement_with_contract_still_creates_firm_on_confirm()
    {
        var s = await SeedAsync();
        var invoice = await CreateAsync(s, PurchaseInvoiceType.ValueComplement, s.ContractUsage, s.Contract.Key);

        await ConfirmAsync(s, invoice);

        var title = Assert.Single(await TitlesAsync(s, invoice));
        Assert.Equal(FinancialDocumentOrigin.PurchaseInvoice, title.OriginType);
        Assert.Equal(s.Contract.Key, title.PurchaseContractKey);
        Assert.Equal(1_500m, title.NetAmount);
    }

    [Theory]
    [InlineData("nonfinancial")]
    [InlineData("other")]
    [InlineData("contract")]
    public async Task Icms_complement_creates_no_title(string kind)
    {
        var s = await SeedAsync();
        var (usage, contract) = kind switch
        {
            "other" => (s.OtherPurchase, (Guid?)null),
            "contract" => (s.ContractUsage, s.Contract.Key),
            _ => (s.NonFinancial, (Guid?)null),
        };
        var invoice = await CreateAsync(s, PurchaseInvoiceType.IcmsComplement, usage, contract);

        await ConfirmAsync(s, invoice);

        Assert.Equal(InvoiceStatus.Confirmed,
            (await s.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).InvoiceStatus);
        Assert.Empty(await s.Db.Context.FinancialDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Icms_complement_line_is_not_offered_for_reconciliation()
    {
        var fiscal = await FiscalReconciliationSeed.CreateAsync();
        await fiscal.AddInvoiceAsync(0m, type: PurchaseInvoiceType.IcmsComplement, taxNumber: "5002");

        var lines = await FiscalReconciliationSeed.NewService(fiscal.Db).ListInvoiceLinesAsync(
            new FiscalReconciliationFilter { CardCode = FiscalReconciliationSeed.Supplier, ItemCode = FiscalReconciliationSeed.Product });

        Assert.Empty(lines);
    }

    [Fact]
    public async Task Icms_complement_line_cannot_be_linked()
    {
        var fiscal = await FiscalReconciliationSeed.CreateAsync();
        var tx = await fiscal.AddTransactionAsync(10_000m);
        var (_, line) = await fiscal.AddInvoiceAsync(0m, type: PurchaseInvoiceType.IcmsComplement, taxNumber: "5002");

        var error = await Assert.ThrowsAsync<DefaultException>(() => FiscalReconciliationSeed.NewService(fiscal.Db).SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = tx.Key, Quantity = 10m }], "ana"));

        Assert.Equal("O documento 5002-1 é complemento de ICMS: não se concilia com romaneio.", error.Message);
    }

    [Fact]
    public void Third_party_icms_complement_requires_supplier_key_and_declares_zero()
    {
        var invoice = new PurchaseInvoice
        {
            InvoiceType = PurchaseInvoiceType.IcmsComplement, IssuerType = DocumentIssuerType.ThirdParty,
            CardCode = FiscalReconciliationSeed.Supplier, TaxDocumentKind = TaxDocumentKind.Nfe,
        };
        invoice.AddItem(new PurchaseInvoiceItem { IcmsBase = 1_000m, IcmsValue = 120m });

        Assert.True(SupplierNfeKeyGuard.AppliesTo(invoice));
        Assert.True(PurchaseInvoiceDeclaredTotal.AppliesTo(invoice));
        PurchaseInvoiceDeclaredTotal.Apply(invoice, invoice.Items);
        Assert.Equal(0m, invoice.TotalDocumentValue);
    }
}
