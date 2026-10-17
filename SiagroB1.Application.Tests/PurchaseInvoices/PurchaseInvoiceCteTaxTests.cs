using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-17: tributos do CT-e pelas UFs da prestação, fora do DIFAL.</summary>
public class PurchaseInvoiceCteTaxTests
{
    private const string Carrier = "F-TRANSP";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private sealed record Seed(int FreightUsage, int ConsumptionUsage);

    private async Task<Seed> SeedAsync()
    {
        _db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "FILIAL", ShortName = "FILIAL", TaxId = "12345678000195", StateCode = "SP",
            StateRegistration = "111222333444", TaxRegime = TaxRegime.Normal, IssuesNfe = true,
        });
        _db.Context.Items.Add(new Item { ItemCode = "FRETE", ItemName = "FRETE", Ncm = "00000000", GoodsOrigin = 0 });
        _db.Context.IcmsStateSettings.Add(new IcmsStateSetting
            { StateCode = "SP", InternalRate = 18m, FcpRate = 0m, DifalBaseRule = DifalBaseRule.Double });

        var usages = new UsageService(_db, NullLogger<UsageService>.Instance);
        var freight = await usages.CreateAsync(new UsageModel
        {
            Name = "FRETE SOBRE VENDAS", Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1353", CfopIncomingOutState = "2353",
            IcmsInStateCst = "00", IcmsInStateRate = 12m, IcmsOutStateCst = "00", PisCst = "50", CofinsCst = "50",
        });
        var consumption = await usages.CreateAsync(new UsageModel
        {
            Name = "USO E CONSUMO", Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1556", CfopIncomingOutState = "2556",
            IcmsInStateCst = "00", IcmsInStateRate = 12m, IcmsOutStateCst = "00", PisCst = "50", CofinsCst = "50",
            PurchasePurpose = PurchasePurpose.UseAndConsumption,
        });
        await _db.SaveChangesAsync();
        return new Seed(freight.Code, consumption.Code);
    }

    private static PurchaseInvoice Cte(int usage, string start, string end, string? cardCode = Carrier)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = cardCode, IssuerType = DocumentIssuerType.ThirdParty,
            InvoiceType = PurchaseInvoiceType.Normal, TaxDocumentKind = TaxDocumentKind.Cte,
            FreightStartState = start, FreightEndState = end, InvoiceStatus = InvoiceStatus.Pending,
            IssueDate = new DateTime(2026, 10, 12),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "FRETE", UnitOfMeasureCode = "UN", Quantity = 1m, UnitPrice = 1000m,
            UsageCode = usage,
        });
        return invoice;
    }

    private PurchaseInvoicesTaxApplyService Apply(string carrierState = "RJ") => TaxTestServices.PurchaseApply(
        _db, new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [Carrier] = "TRANSPORTADORA" },
            states: new Dictionary<string, string> { [Carrier] = carrierState }));

    private async Task<PurchaseInvoiceItem> RunAsync(PurchaseInvoice invoice, string carrierState = "RJ")
    {
        await Apply(carrierState).ApplyAsync(invoice, invoice.Items);
        return invoice.Items.Single();
    }

    [Fact]
    public async Task Freight_starting_in_branch_state_to_other_state_uses_in_state_cfop_and_interstate_rate()
    {
        var s = await SeedAsync();

        var line = await RunAsync(Cte(s.FreightUsage, "SP", "MT"));

        Assert.Equal("1353", line.Cfop);
        Assert.Equal(7m, line.IcmsRate);
    }

    [Fact]
    public async Task Freight_starting_in_other_state_uses_out_state_cfop()
    {
        var s = await SeedAsync();

        var line = await RunAsync(Cte(s.FreightUsage, "PR", "SP"));

        Assert.Equal("2353", line.Cfop);
        Assert.Equal(12m, line.IcmsRate);
    }

    [Fact]
    public async Task Freight_inside_other_state_uses_out_state_cfop_and_internal_rule()
    {
        var s = await SeedAsync();

        var line = await RunAsync(Cte(s.FreightUsage, "PR", "PR"));

        Assert.Equal("2353", line.Cfop);
        Assert.Equal(12m, line.IcmsRate);
        Assert.Equal("00", line.CstIcms);
    }

    [Fact]
    public async Task Freight_inside_branch_state_is_fully_internal()
    {
        var s = await SeedAsync();

        var line = await RunAsync(Cte(s.FreightUsage, "SP", "SP"));

        Assert.Equal("1353", line.Cfop);
        Assert.Equal(12m, line.IcmsRate);
    }

    [Fact]
    public async Task Cte_has_no_difal_even_with_use_and_consumption_usage()
    {
        var s = await SeedAsync();
        var invoice = Cte(s.ConsumptionUsage, "PR", "SP");
        await _db.SaveChangesAsync();

        var outcomes = await PurchaseInvoiceDifal.EvaluateAsync(
            _db.Context, new UsageService(_db, NullLogger<UsageService>.Instance),
            new FakeBusinessPartnerService(states: new Dictionary<string, string> { [Carrier] = "PR" }),
            standalone: true, invoice, invoice.Items);

        Assert.All(outcomes, o => { Assert.Null(o.Error); Assert.Equal(DifalResult.None, o.Result); });
    }

    [Fact]
    public async Task Nfe_calculation_is_unchanged()
    {
        var s = await SeedAsync();
        var invoice = Cte(s.FreightUsage, "PR", "PR");
        invoice.TaxDocumentKind = TaxDocumentKind.Nfe;
        invoice.FreightStartState = null;
        invoice.FreightEndState = null;

        // Fornecedor em RJ, filial em SP: CFOP fora do estado e alíquota pela UF do fornecedor (RJ → SP = 12%).
        var line = await RunAsync(invoice, carrierState: "RJ");

        Assert.Equal("2353", line.Cfop);
        Assert.Equal(12m, line.IcmsRate);
    }
}
