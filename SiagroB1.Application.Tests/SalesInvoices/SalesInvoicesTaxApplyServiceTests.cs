using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Orquestração do cálculo: carrega natureza, filial, cliente, produto e alíquota, barra o que
/// falta com mensagem de negócio e grava a fotografia. Só age com a regra ativa.
/// </summary>
public class SalesInvoicesTaxApplyServiceTests
{
    private const string CardBa = "C-BA";
    private const string CardSp = "C-SP";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [CardBa] = "CLIENTE BA", [CardSp] = "CLIENTE SP", ["C-SEM-UF"] = "SEM UF" },
        states: new() { [CardBa] = "BA", [CardSp] = "SP" });

    private static async Task<(UnitOfWork db, int usageCode)> Seed(
        bool issuesNfe = true, TaxRegime? regime = TaxRegime.Normal, byte? origin = 0, string? ncm = "12019000",
        bool withRate = true, Action<UsageModel>? tweak = null)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", StateCode = "SP", TaxRegime = regime, IssuesNfe = issuesNfe,
        });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA", GoodsOrigin = origin, Ncm = ncm });
        if (withRate)
            db.Context.IbsCbsRates.Add(new IbsCbsRate
            {
                StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m,
            });
        await db.SaveChangesAsync();

        var usage = new UsageModel
        {
            Name = "Venda de grãos", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsOutStateCst = "00",
            PisCst = "01", PisRate = 1.65m, CofinsCst = "01", CofinsRate = 7.6m,
            ExcludeIcmsFromPisCofinsBase = true,
            IbsCbsCst = "000", IbsCbsClassCode = "000001",
            MovesFiscalInventory = true, CreatesFinancialDocument = true,
            RequiresQuantity = true, IsDefault = true,
        };
        tweak?.Invoke(usage);
        var created = await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(usage);

        return (db, created.Code);
    }

    private static SalesInvoice Invoice(string cardCode = CardBa, int? usageCode = null) => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", CardCode = cardCode, InvoiceDate = new DateTime(2026, 10, 1),
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG",
                Quantity = 30000m, UnitPrice = 2m, UsageCode = usageCode,
            },
        ],
    };

    private static async Task<SalesInvoiceItem> Apply(UnitOfWork db, SalesInvoice invoice)
    {
        await TaxTestServices.Apply(db, Partners()).ApplyAsync(invoice, invoice.Items);
        return invoice.Items.Single();
    }

    private static async Task Rejects(UnitOfWork db, SalesInvoice invoice, string expected)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            TaxTestServices.Apply(db, Partners()).ApplyAsync(invoice, invoice.Items));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Interstate_line_gets_the_reference_example_snapshot()
    {
        var (db, code) = await Seed();
        var item = await Apply(db, Invoice(CardBa, code));

        Assert.Equal("6102", item.Cfop);
        Assert.Equal("12019000", item.Ncm);
        Assert.Equal((byte)0, item.GoodsOrigin);
        Assert.Equal("00", item.CstIcms);
        Assert.Equal(7m, item.IcmsRate);
        Assert.Equal(4200.00m, item.IcmsValue);
        Assert.Equal(920.70m, item.PisValue);
        Assert.Equal(4240.80m, item.CofinsValue);
        Assert.Equal(455.75m, item.CbsValue);
        Assert.Equal(50.64m, item.IbsStateValue);
        Assert.True(item.MovesFiscalInventory);
        Assert.True(item.CreatesFinancialDocument);
        Assert.Equal("Venda de grãos", item.UsageName);
    }

    [Fact]
    public async Task In_state_line_uses_the_in_state_block()
    {
        var (db, code) = await Seed();
        var item = await Apply(db, Invoice(CardSp, code));

        Assert.Equal("5102", item.Cfop);
        Assert.Equal("51", item.CstIcms);
        Assert.Equal(10800.00m, item.IcmsDeferredValue);
        Assert.Equal(0m, item.IcmsValue);
    }

    [Fact]
    public async Task Line_without_usage_falls_back_to_the_default()
    {
        var (db, code) = await Seed();
        var item = await Apply(db, Invoice(CardBa, usageCode: null));
        Assert.Equal(code, item.UsageCode);
    }

    [Fact]
    public async Task Line_without_usage_and_without_default_is_rejected()
    {
        var (db, _) = await Seed(tweak: u => u.IsDefault = false);
        await Rejects(db, Invoice(CardBa, usageCode: null), "sem natureza de operação");
    }

    [Fact]
    public async Task Incoming_usage_is_rejected_on_a_sales_invoice()
    {
        var (db, _) = await Seed();
        var incoming = await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Compra de produtor", Direction = UsageDirection.Incoming, CfopIncomingInState = "1102",
            PisCst = "50", CofinsCst = "50", IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            RequiresQuantity = true,
        });

        await Rejects(db, Invoice(CardSp, incoming.Code), "é de entrada");
    }

    [Fact]
    public async Task Usage_without_icms_code_for_the_block_is_rejected()
    {
        var (db, code) = await Seed(tweak: u => u.IcmsOutStateCst = null);
        await Rejects(db, Invoice(CardBa, code), "sem CST de ICMS fora do estado");
    }

    [Fact]
    public async Task Simples_nacional_requires_the_csosn()
    {
        var (db, code) = await Seed(regime: TaxRegime.SimplesNacional);
        await Rejects(db, Invoice(CardBa, code), "sem CSOSN de ICMS fora do estado");
    }

    [Fact]
    public async Task Usage_without_pis_cofins_cst_is_rejected()
    {
        var (db, code) = await Seed(tweak: u => u.CofinsCst = null);
        await Rejects(db, Invoice(CardBa, code), "CST de PIS/COFINS");
    }

    [Fact]
    public async Task Branch_without_tax_regime_is_rejected()
    {
        var (db, code) = await Seed(regime: null);
        await Rejects(db, Invoice(CardBa, code), "regime tributário");
    }

    [Fact]
    public async Task Customer_without_state_is_rejected()
    {
        var (db, code) = await Seed();
        await Rejects(db, Invoice("C-SEM-UF", code), "sem UF");
    }

    [Fact]
    public async Task Product_without_ncm_is_rejected()
    {
        var (db, code) = await Seed(ncm: null);
        await Rejects(db, Invoice(CardBa, code), "sem NCM");
    }

    [Fact]
    public async Task Product_without_origin_is_rejected_even_in_state()
    {
        var (db, code) = await Seed(origin: null);
        await Rejects(db, Invoice(CardSp, code), "sem origem");
    }

    [Fact]
    public async Task Date_before_the_first_ibs_cbs_validity_is_rejected()
    {
        var (db, code) = await Seed(withRate: false);
        await Rejects(db, Invoice(CardBa, code), "01/10/2026");
    }

    [Fact]
    public async Task Missing_ibs_cbs_rate_is_fine_when_the_usage_has_no_ibs_cbs()
    {
        var (db, code) = await Seed(withRate: false, tweak: u => { u.IbsCbsCst = null; u.IbsCbsClassCode = null; });
        var item = await Apply(db, Invoice(CardBa, code));
        Assert.Null(item.IbsCbsCst);
        Assert.Equal(0m, item.CbsValue);
    }

    [Fact]
    public async Task Inactive_rule_leaves_typed_values_untouched()
    {
        var (db, code) = await Seed(issuesNfe: false);
        var invoice = Invoice(CardBa, code);
        invoice.Items.Single().IcmsValue = 123m;
        invoice.Items.Single().Cfop = "6949";

        var item = await Apply(db, invoice);

        Assert.Equal(123m, item.IcmsValue);
        Assert.Equal("6949", item.Cfop);
    }

    [Fact]
    public async Task Sapb1_with_switch_forced_on_does_nothing()
    {
        var (db, code) = await Seed();
        var invoice = Invoice(CardBa, code);
        invoice.Items.Single().IcmsValue = 123m;

        await TaxTestServices.Apply(db, Partners(), "SAPB1").ApplyAsync(invoice, invoice.Items);

        Assert.Equal(123m, invoice.Items.Single().IcmsValue);
    }

    [Fact]
    public async Task Return_invoices_are_skipped()
    {
        var (db, code) = await Seed();
        var invoice = Invoice(CardBa, code);
        invoice.InvoiceType = SalesInvoiceType.Return;
        invoice.Items.Single().IcmsValue = 123m;

        var item = await Apply(db, invoice);
        Assert.Equal(123m, item.IcmsValue);
    }

    [Fact]
    public async Task Confirmed_invoices_are_not_recalculated()
    {
        var (db, code) = await Seed();
        var invoice = Invoice(CardBa, code);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.Items.Single().IcmsValue = 123m;

        var item = await Apply(db, invoice);
        Assert.Equal(123m, item.IcmsValue);
    }

    // --- Frete, seguro, desconto e outras despesas na base (spec 2026-10-05 D3) ---

    private static SalesInvoice InvoiceWithCharges(int usageCode)
    {
        var invoice = Invoice(CardBa, usageCode);
        var item = invoice.Items.Single();
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (1000m, 100m, 500m, 400m);
        return invoice;
    }

    [Fact]
    public async Task Charges_enter_the_base_of_every_tax()
    {
        var (db, code) = await Seed();

        var item = await Apply(db, InvoiceWithCharges(code));

        Assert.Equal(61000m, item.GrandTotal);
        Assert.Equal((61000m, 4270.00m), (item.IcmsBase, item.IcmsValue));
        Assert.Equal((56730.00m, 936.05m), (item.PisBase, item.PisValue));
        Assert.Equal((56730.00m, 4311.48m), (item.CofinsBase, item.CofinsValue));
        Assert.Equal((51482.47m, 463.34m, 51.48m), (item.IbsCbsBase, item.CbsValue, item.IbsStateValue));
    }

    [Fact]
    public async Task Icms_base_reduction_applies_to_the_grand_total_of_the_line()
    {
        // Review Focus 4: CST 20 com 40% de redução — 61.000,00 × 60% = 36.600,00; 7% = 2.562,00.
        var (db, code) = await Seed(tweak: u =>
        {
            u.IcmsOutStateCst = "20";
            u.IcmsOutStateBaseReduction = 40m;
        });

        var item = await Apply(db, InvoiceWithCharges(code));

        Assert.Equal((40m, 36600.00m, 2562.00m), (item.IcmsBaseReduction, item.IcmsBase, item.IcmsValue));
    }

    [Fact]
    public async Task Branch_without_the_rule_only_keeps_the_charges()
    {
        var (db, code) = await Seed();
        var invoice = InvoiceWithCharges(code);

        await TaxTestServices.Apply(db, Partners(), "SAPB1").ApplyAsync(invoice, invoice.Items);

        var item = invoice.Items.Single();
        Assert.Equal((1000m, 0m, (string?)null), (item.FreightValue, item.IcmsBase, item.CstIcms));
    }
}
