using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Ciclo do CT-e de frete tomado: título a pagar para a transportadora, estorno e unicidade da chave (spec 2026-10-17).</summary>
public class PurchaseInvoiceCteLifecycleTests
{
    private static string CteKey()
    {
        var first43 = "3526" + "10" + "00052998224725" + "57" + "001" + "000000456" + "1" + "12345678";
        return first43 + SupplierNfeKeyGuard.CheckDigit(first43);
    }

    private sealed class Fixture
    {
        public UnitOfWork Db = null!;
        public FakeBusinessPartnerService Partners = PurchaseNfeTestSeed.Partners();
        public FakeDocNumberSequenceService Numbers = new();
        public int OtherPurchaseUsage;
        public int ContractUsage;
        public int ConditionCode;

        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture { Db = (await PurchaseNfeTestSeed.SeedAsync()).Db };
            var usages = new UsageService(f.Db, NullLogger<UsageService>.Instance);
            f.OtherPurchaseUsage = (await usages.CreateAsync(new UsageModel
            {
                Name = "FRETE TOMADO", Direction = UsageDirection.Incoming, CfopIncomingInState = "1353", CfopIncomingOutState = "2353",
                IcmsInStateCst = "00", IcmsInStateRate = 12m, IcmsOutStateCst = "00", PisCst = "50", CofinsCst = "50",
                CreatesFinancialDocument = true,
            })).Code;
            f.ContractUsage = (await usages.CreateAsync(new UsageModel
            {
                Name = "FRETE COM CONTRATO", Direction = UsageDirection.Incoming, CfopIncomingInState = "1353", CfopIncomingOutState = "2353",
                IcmsInStateCst = "00", IcmsInStateRate = 12m, IcmsOutStateCst = "00", PisCst = "50", CofinsCst = "50",
                CreatesFinancialDocument = true, RequiresContract = true,
            })).Code;
            var condition = new PaymentCondition { Name = "30/60", Days = "30,60", PaymentMeans = "15" };
            f.Db.Context.PaymentConditions.Add(condition);
            await f.Db.Context.SaveChangesAsync();
            f.ConditionCode = condition.Code;
            return f;
        }

        public PurchaseInvoice Cte(int usage, Guid? key = null)
        {
            var invoice = new PurchaseInvoice
            {
                Key = key ?? Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
                IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
                TaxDocumentKind = TaxDocumentKind.Cte, ChaveNFe = CteKey(), TaxDocumentNumber = "456", TaxDocumentSeries = "1",
                IssueDate = new DateTime(2026, 10, 1), FreightStartState = "SP", FreightEndState = "PR",
                FreightPayerTaxId = "12345678000195", PaymentConditionCode = ConditionCode,
            };
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1500m, UsageCode = usage,
            });
            return invoice;
        }

        public Task CreateAsync(PurchaseInvoice invoice) =>
            new PurchaseInvoicesCreateService(Db, Partners, new FakeItemService(),
                    TaxTestServices.PurchaseApply(Db, Partners), new FakeDocNumberSequenceService())
                .ExecuteAsync(invoice, "ana");

        public Task ConfirmAsync(PurchaseInvoice invoice) =>
            TaxTestServices.PurchaseConfirm(Db, "STANDALONE", Partners, new FakeNfeSefazClient(), Numbers).ExecuteAsync(invoice.Key, "ana");

        public Task<List<FinancialDocument>> TitlesAsync(PurchaseInvoice invoice) =>
            Db.Context.FinancialDocuments.AsNoTracking()
                .Where(d => d.OriginType == FinancialDocumentOrigin.OtherPurchaseInvoice && d.OriginKey == invoice.Key)
                .OrderBy(d => d.InstallmentNumber).ToListAsync();
    }

    [Fact]
    public async Task Confirming_cte_with_other_purchase_usage_creates_payable_to_carrier()
    {
        var f = await Fixture.CreateAsync();
        var invoice = f.Cte(f.OtherPurchaseUsage);
        await f.CreateAsync(invoice);

        await f.ConfirmAsync(invoice);

        var titles = await f.TitlesAsync(invoice);
        Assert.Equal(new[] { 750m, 750m }, titles.Select(d => d.NetAmount));
        Assert.All(titles, d =>
        {
            Assert.Equal(FinancialDirection.Payable, d.Direction);
            Assert.Equal(PurchaseNfeTestSeed.Supplier, d.CardCode);
            Assert.Equal(FinancialDocumentOrigin.OtherPurchaseInvoice, d.OriginType);
            Assert.Equal(FinancialDocumentStatus.Open, d.Status);
        });
    }

    [Fact]
    public async Task Reversing_cte_cancels_open_titles()
    {
        var f = await Fixture.CreateAsync();
        var invoice = f.Cte(f.OtherPurchaseUsage);
        await f.CreateAsync(invoice);
        await f.ConfirmAsync(invoice);

        await FinancialDocumentTestServices.PurchaseReverse(f.Db).ExecuteAsync(invoice.Key, "bia");

        var titles = await f.TitlesAsync(invoice);
        Assert.Equal(2, titles.Count);
        Assert.All(titles, d => Assert.Equal(FinancialDocumentStatus.Canceled, d.Status));
    }

    [Fact]
    public async Task Same_cte_key_twice_is_rejected()
    {
        var f = await Fixture.CreateAsync();
        var first = f.Cte(f.OtherPurchaseUsage);
        await f.CreateAsync(first);
        await f.ConfirmAsync(first);

        var second = f.Cte(f.OtherPurchaseUsage);
        var e = await Assert.ThrowsAsync<DefaultException>(() => f.CreateAsync(second));

        Assert.Equal($"Já existe documento de entrada com a chave de acesso {CteKey()}.", e.Message);
        Assert.Equal(2, (await f.TitlesAsync(first)).Count);
        Assert.Empty(await f.TitlesAsync(second));
    }

    [Fact]
    public async Task Cte_with_contract_requiring_usage_is_rejected()
    {
        var f = await Fixture.CreateAsync();
        var invoice = f.Cte(f.ContractUsage);

        var e = await Assert.ThrowsAsync<DefaultException>(() => f.CreateAsync(invoice));

        Assert.Equal("A natureza FRETE COM CONTRATO exige contrato de compra na linha do produto TRIGO.", e.Message);
    }
}
