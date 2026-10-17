using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Spec 2026-10-17 §3: a confirmação do documento de "outra compra" (STANDALONE, sem contrato, natureza que gera
/// financeiro) cria as parcelas a pagar do fornecedor sobre o líquido e um título por tributo retido para o credor.
/// </summary>
public class FinancialDocumentsOtherPurchaseTests
{
    private const string Supplier = "F001";
    private const string CityCreditor = "TRIB-MUNICIPAL";
    private const string FederalCreditor = "TRIB-FEDERAL";
    private static readonly DateTime Issue = new(2026, 1, 15);

    private sealed class Fixture(FakeBusinessPartnerService? partners = null)
    {
        public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
        public FakeDocNumberSequenceService Numbers { get; } = new();

        public FakeBusinessPartnerService Partners { get; } = partners ?? new FakeBusinessPartnerService(new Dictionary<string, string>
        {
            [Supplier] = "FORNECEDOR", [CityCreditor] = "PREFEITURA", [FederalCreditor] = "RECEITA FEDERAL",
        });

        public int ServiceUsage { get; private set; }
        public int ContractUsage { get; private set; }

        public static async Task<Fixture> CreateAsync(bool settings = true, FakeBusinessPartnerService? partners = null)
        {
            var f = new Fixture(partners);
            var usages = new UsageService(f.Db, NullLogger<UsageService>.Instance);
            f.ServiceUsage = (await usages.CreateAsync(new UsageModel
            {
                Name = "Serviço tomado", Direction = UsageDirection.Incoming, CfopIncomingInState = "1933",
                CfopIncomingOutState = "2933", CreatesFinancialDocument = true, WithholdingIssRate = 5m, WithholdingIrrfRate = 1.5m,
            })).Code;
            f.ContractUsage = (await usages.CreateAsync(new UsageModel
            {
                Name = "Compra de grão", Direction = UsageDirection.Incoming, CfopIncomingInState = "1102",
                CfopIncomingOutState = "2102", CreatesFinancialDocument = true, RequiresContract = true,
            })).Code;
            if (settings)
            {
                f.Db.Context.WithholdingTaxSettings.AddRange(
                    new WithholdingTaxSetting { TaxType = (int)WithholdingTaxType.Iss, CardCode = CityCreditor, CardName = "PREFEITURA", DueDay = 10 },
                    new WithholdingTaxSetting { TaxType = (int)WithholdingTaxType.Irrf, CardCode = FederalCreditor, CardName = "RECEITA FEDERAL", DueDay = 20 });
                await f.Db.Context.SaveChangesAsync();
            }
            return f;
        }

        public async Task<int> AddConditionAsync(string days, string means = "15")
        {
            var condition = new PaymentCondition { Name = days, Days = days, PaymentMeans = means };
            Db.Context.PaymentConditions.Add(condition);
            await Db.Context.SaveChangesAsync();
            return condition.Code;
        }

        public async Task<PurchaseInvoice> CreateInvoiceAsync(
            decimal price = 1000m, int? condition = null, DateTime? issue = null, string erp = "STANDALONE",
            Action<PurchaseInvoice>? tweak = null)
        {
            var invoice = new PurchaseInvoice
            {
                Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, InvoiceType = PurchaseInvoiceType.Normal,
                IssuerType = DocumentIssuerType.ThirdParty, TaxDocumentNumber = "7001", TaxDocumentSeries = "1",
                IssueDate = issue ?? Issue, PaymentConditionCode = condition,
            };
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SERV", ItemName = "SERVICO", UnitOfMeasureCode = "UN", Quantity = 1m,
                UnitPrice = price, UsageCode = ServiceUsage,
            });
            tweak?.Invoke(invoice);
            await CreateAsync(invoice, erp);
            return invoice;
        }

        public Task CreateAsync(PurchaseInvoice invoice, string erp = "STANDALONE") =>
            new PurchaseInvoicesCreateService(Db, Partners, new FakeItemService(),
                    TaxTestServices.PurchaseApply(Db, Partners, erp), new FakeDocNumberSequenceService())
                .ExecuteAsync(invoice, "ana");

        public Task ConfirmAsync(PurchaseInvoice invoice, string erp = "STANDALONE") =>
            TaxTestServices.PurchaseConfirm(Db, erp, Partners, firmNumbers: Numbers).ExecuteAsync(invoice.Key, "ana");

        public Task<List<FinancialDocument>> TitlesAsync(PurchaseInvoice invoice) =>
            Db.Context.FinancialDocuments.AsNoTracking()
                .Where(d => d.OriginType == FinancialDocumentOrigin.OtherPurchaseInvoice && d.OriginKey == invoice.Key)
                .OrderBy(d => d.Nature).ThenBy(d => d.InstallmentNumber).ThenBy(d => d.WithholdingTaxType).ToListAsync();
    }

    [Fact]
    public async Task Confirm_creates_installments_from_payment_condition()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync(condition: await f.AddConditionAsync("30,60"));

        await f.ConfirmAsync(invoice);

        var installments = (await f.TitlesAsync(invoice)).Where(d => d.Nature == FinancialDocumentNature.Firm).ToList();
        Assert.Equal(new[] { 467.50m, 467.50m }, installments.Select(d => d.NetAmount));
        Assert.Equal(new[] { Issue.AddDays(30), Issue.AddDays(60) }, installments.Select(d => d.DueDate));
        Assert.Equal(new int?[] { 1, 2 }, installments.Select(d => d.InstallmentNumber));
        Assert.All(installments, d =>
        {
            Assert.Equal(FinancialDirection.Payable, d.Direction);
            Assert.Equal(Supplier, d.CardCode);
            Assert.Equal("FORNECEDOR", d.CardName);
            Assert.Equal(invoice.Key, d.PurchaseInvoiceKey);
            Assert.Equal("7001-1", d.TaxDocumentNumber);
            Assert.Equal(CurrencyType.Brl, d.Currency);
            Assert.Equal(Issue, d.DocumentDate);
            Assert.Equal(FinancialDocumentStatus.Open, d.Status);
            Assert.Equal(d.NetAmount, d.OpenAmount);
        });
    }

    [Fact]
    public async Task Installment_rounding_closes_on_net()
    {
        var f = await Fixture.CreateAsync();
        // Retenção manual zerada: líquido = 1.000,00.
        var invoice = await f.CreateInvoiceAsync(condition: await f.AddConditionAsync("30,60,90"), tweak: i =>
        {
            i.WithholdingsManual = true;
            i.WithholdingBase = 1000m;
        });

        await f.ConfirmAsync(invoice);

        var installments = await f.TitlesAsync(invoice);
        Assert.Equal(new[] { 333.33m, 333.33m, 333.34m }, installments.Select(d => d.NetAmount));
    }

    [Fact]
    public async Task Confirm_creates_one_withholding_document_per_tax()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync();

        await f.ConfirmAsync(invoice);

        var taxes = (await f.TitlesAsync(invoice)).Where(d => d.Nature == FinancialDocumentNature.TaxWithholding).ToList();
        Assert.Equal(2, taxes.Count);
        var iss = taxes.Single(d => d.WithholdingTaxType == WithholdingTaxType.Iss);
        var irrf = taxes.Single(d => d.WithholdingTaxType == WithholdingTaxType.Irrf);
        Assert.Equal((CityCreditor, "PREFEITURA", 50m, new DateTime(2026, 2, 10)), (iss.CardCode, iss.CardName, iss.NetAmount, iss.DueDate));
        Assert.Equal((FederalCreditor, "RECEITA FEDERAL", 15m, new DateTime(2026, 2, 20)), (irrf.CardCode, irrf.CardName, irrf.NetAmount, irrf.DueDate));
        Assert.All(taxes, d =>
        {
            Assert.Equal(FinancialDirection.Payable, d.Direction);
            Assert.Equal(invoice.Key, d.PurchaseInvoiceKey);
            Assert.Null(d.InstallmentNumber);
        });
    }

    [Fact]
    public async Task Due_day_beyond_month_end_is_clamped()
    {
        var f = await Fixture.CreateAsync();
        (await f.Db.Context.WithholdingTaxSettings.SingleAsync(s => s.TaxType == (int)WithholdingTaxType.Iss)).DueDay = 31;
        await f.Db.Context.SaveChangesAsync();
        var invoice = await f.CreateInvoiceAsync(issue: new DateTime(2026, 1, 15));

        await f.ConfirmAsync(invoice);

        var iss = (await f.TitlesAsync(invoice)).Single(d => d.WithholdingTaxType == WithholdingTaxType.Iss);
        Assert.Equal(new DateTime(2026, 2, 28), iss.DueDate);
    }

    [Fact]
    public async Task No_payment_condition_creates_single_title_due_on_issue()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync();

        await f.ConfirmAsync(invoice);

        var installment = (await f.TitlesAsync(invoice)).Single(d => d.Nature == FinancialDocumentNature.Firm);
        Assert.Equal((935m, Issue, (int?)1), (installment.NetAmount, installment.DueDate, installment.InstallmentNumber));
    }

    [Fact]
    public async Task No_payment_means_condition_creates_single_title_due_on_issue()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync(condition: await f.AddConditionAsync("30,60", means: "90"));

        await f.ConfirmAsync(invoice);

        var installment = (await f.TitlesAsync(invoice)).Single(d => d.Nature == FinancialDocumentNature.Firm);
        Assert.Equal((935m, Issue), (installment.NetAmount, installment.DueDate));
    }

    [Fact]
    public async Task Withholdings_consuming_whole_invoice_are_refused_and_nothing_is_saved()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync(tweak: i =>
        {
            i.WithholdingsManual = true;
            i.WithholdingBase = 1000m;
            i.WithheldIss = 1000m;
        });

        var error = await Assert.ThrowsAsync<DefaultException>(() => f.ConfirmAsync(invoice));

        Assert.Equal("O total das retenções (1.000,00) consome o valor da nota: revise os valores retidos.", error.Message);
        Assert.Empty(await f.TitlesAsync(invoice));
        var saved = await f.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
    }

    [Fact]
    public async Task Missing_withholding_setting_is_refused()
    {
        var f = await Fixture.CreateAsync(settings: false);
        var invoice = await f.CreateInvoiceAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(() => f.ConfirmAsync(invoice));

        Assert.Equal("Configure o credor da retenção de ISS em Configuração de Retenções.", error.Message);
        Assert.Empty(await f.TitlesAsync(invoice));
        Assert.Equal(InvoiceStatus.Pending,
            (await f.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Missing_withholding_creditor_partner_is_refused()
    {
        var f = await Fixture.CreateAsync(partners: new FakeBusinessPartnerService(new Dictionary<string, string>
            { [Supplier] = "FORNECEDOR", [FederalCreditor] = "RECEITA FEDERAL" }));
        var invoice = await f.CreateInvoiceAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(() => f.ConfirmAsync(invoice));

        Assert.Equal($"O credor {CityCreditor} da retenção de ISS não existe mais: revise Configuração de Retenções.", error.Message);
        Assert.Empty(await f.TitlesAsync(invoice));
    }

    [Fact]
    public async Task Other_purchase_title_accepts_direct_settlement()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync();
        await f.ConfirmAsync(invoice);
        var titles = await f.TitlesAsync(invoice);
        var account = new FinancialAccount { Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl };

        Assert.Equal(3, titles.Count);
        Assert.All(titles, d =>
        {
            Assert.False(d.IsBlockedForSettlement);
            Assert.False(d.IsContractFirmDocument);
            FinancialDocumentsSettlementGuardService.EnsureCanSettle(d, account, d.OpenAmount);
        });
    }

    [Fact]
    public async Task Contract_documents_create_no_other_purchase_titles()
    {
        var f = await Fixture.CreateAsync();
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-9", CardCode = Supplier, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "25/26", Status = ContractStatus.Approved, TotalVolume = 1_000m,
            FreightTerms = FreightTerms.Cif, DeliveryLocationCode = "01",
        };
        f.Db.Context.PurchaseContracts.Add(contract);
        await f.Db.Context.SaveChangesAsync();
        var grain = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, InvoiceType = PurchaseInvoiceType.Normal,
            IssuerType = DocumentIssuerType.ThirdParty, TaxDocumentNumber = "8001", TaxDocumentSeries = "1", IssueDate = Issue,
        };
        grain.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", ItemName = "SOJA", UnitOfMeasureCode = "KG", Quantity = 100m,
            UnitPrice = 2m, UsageCode = f.ContractUsage, PurchaseContractKey = contract.Key,
        });
        await f.CreateAsync(grain);

        await f.ConfirmAsync(grain);

        Assert.Empty(await f.Db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.OtherPurchaseInvoice).ToListAsync());
        Assert.Equal(InvoiceStatus.Confirmed,
            (await f.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == grain.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Value_complement_keeps_its_firm_and_creates_no_other_purchase_titles()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier,
            InvoiceType = PurchaseInvoiceType.ValueComplement, IssuerType = DocumentIssuerType.ThirdParty,
            TaxDocumentNumber = "5001", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 12),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            ComplementAmount = 1_000m, UsageCode = seed.FinancialUsageCode, PurchaseContractKey = seed.Contract.Key,
        });
        await new PurchaseInvoicesCreateService(seed.Db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.PurchaseApply(seed.Db, new FakeBusinessPartnerService(), seed.Erp), new FakeDocNumberSequenceService())
            .ExecuteAsync(invoice, "ana");

        await TaxTestServices.PurchaseConfirm(seed.Db, seed.Erp, firmNumbers: seed.Numbers).ExecuteAsync(invoice.Key, "ana");

        var docs = await seed.Context.FinancialDocuments.AsNoTracking().Where(d => d.PurchaseInvoiceKey == invoice.Key).ToListAsync();
        Assert.Equal(FinancialDocumentOrigin.PurchaseInvoice, Assert.Single(docs).OriginType);
    }

    [Fact]
    public async Task Sapb1_mode_creates_nothing()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync(erp: "SAPB1");

        await f.ConfirmAsync(invoice, erp: "SAPB1");

        Assert.Empty(await f.Db.Context.FinancialDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Enqueue_is_idempotent_for_an_active_title()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync();
        await f.ConfirmAsync(invoice);
        var before = (await f.TitlesAsync(invoice)).Count;
        var lines = await f.Db.Context.PurchaseInvoicesItems.AsNoTracking().Where(l => l.PurchaseInvoiceKey == invoice.Key).ToListAsync();

        await FinancialDocumentTestServices.OtherPurchase(f.Db, f.Numbers, "STANDALONE", f.Partners).EnqueueAsync(invoice, lines, "ana");
        await f.Db.SaveChangesAsync();

        Assert.Equal(before, (await f.TitlesAsync(invoice)).Count);
    }
}
