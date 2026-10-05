using SiagroB1.Fiscal.Taxes;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Tests.Taxes;

/// <summary>
/// Contas da §6 do spec. A ordem é fixa (ICMS → PIS/COFINS → IBS/CBS) porque cada tributo usa o
/// anterior, e cada valor é arredondado antes de entrar no passo seguinte — é o que fecha com a
/// validação da SEFAZ (vBC do IBS/CBS = vProd − vICMS − vPIS − vCOFINS).
/// </summary>
public class TaxCalculatorTests
{
    private static readonly IbsCbsRates Rates2026 = new(0.9m, 0.1m, 0m);

    private static readonly PisCofinsRule PisCofins = new("01", 1.65m, "01", 7.6m, ExcludeIcmsFromBase: true);

    private static TaxCalculationInput Input(
        IcmsRule icms, bool inState = false, decimal amount = 60000m, TaxRegime regime = TaxRegime.Normal,
        byte origin = 0, PisCofinsRule? pisCofins = null, IbsCbsRule? ibsCbs = null, IbsCbsRates? rates = null,
        string branchState = "SP", string customerState = "BA") =>
        new(amount, inState, branchState, inState ? branchState : customerState, regime, origin, icms,
            pisCofins ?? PisCofins, ibsCbs, rates);

    private static IcmsRule Cst(string cst, decimal? rate = null, decimal? reduction = null, decimal? deferral = null) =>
        new(cst, null, rate, reduction, deferral, null);

    /// <summary>O exemplo de referência da §6.2 do spec, valor por valor.</summary>
    [Fact]
    public void Reference_example_interstate_sale_sp_to_ba()
    {
        var r = TaxCalculator.Calculate(Input(
            Cst("00"), ibsCbs: new IbsCbsRule("000", "000001", null, null), rates: Rates2026));

        Assert.Equal("00", r.IcmsCode);
        Assert.Equal(60000.00m, r.IcmsBase);
        Assert.Equal(7m, r.IcmsRate);
        Assert.Equal(4200.00m, r.IcmsValue);
        Assert.Equal(55800.00m, r.PisBase);
        Assert.Equal(920.70m, r.PisValue);
        Assert.Equal(55800.00m, r.CofinsBase);
        Assert.Equal(4240.80m, r.CofinsValue);
        Assert.Equal(50638.50m, r.IbsCbsBase);
        Assert.Equal(455.75m, r.CbsValue);
        Assert.Equal(50.64m, r.IbsStateValue);
        Assert.Equal(0.00m, r.IbsMunicipalValue);
    }

    [Fact]
    public void Reference_example_in_state_with_full_deferral()
    {
        var r = TaxCalculator.Calculate(Input(Cst("51", rate: 18m, deferral: 100m), inState: true));

        Assert.Equal(60000.00m, r.IcmsBase);
        Assert.Equal(18m, r.IcmsRate);
        Assert.Equal(10800.00m, r.IcmsOperationValue);
        Assert.Equal(100m, r.IcmsDeferral);
        Assert.Equal(10800.00m, r.IcmsDeferredValue);
        Assert.Equal(0.00m, r.IcmsValue);
        // Tema 69 com ICMS zero: a base do PIS é o valor cheio.
        Assert.Equal(60000.00m, r.PisBase);
    }

    [Fact]
    public void Partial_deferral_keeps_the_rest_of_the_icms()
    {
        var r = TaxCalculator.Calculate(Input(Cst("51", rate: 18m, deferral: 33.33m), inState: true, amount: 1000m));

        Assert.Equal(180.00m, r.IcmsOperationValue);
        Assert.Equal(59.99m, r.IcmsDeferredValue);
        Assert.Equal(120.01m, r.IcmsValue);
    }

    [Fact]
    public void Cst_20_reduces_the_base()
    {
        var r = TaxCalculator.Calculate(Input(Cst("20", rate: 18m, reduction: 33.33m), inState: true, amount: 1000m));

        Assert.Equal(666.70m, r.IcmsBase);
        Assert.Equal(33.33m, r.IcmsBaseReduction);
        Assert.Equal(120.01m, r.IcmsValue);
    }

    [Fact]
    public void Cst_90_without_reduction_taxes_the_full_amount()
    {
        var r = TaxCalculator.Calculate(Input(Cst("90", rate: 12m), inState: true, amount: 1000m));
        Assert.Equal(1000.00m, r.IcmsBase);
        Assert.Equal(120.00m, r.IcmsValue);
    }

    [Theory]
    [InlineData("40")]
    [InlineData("41")]
    [InlineData("50")]
    public void Exempt_cst_zeroes_the_icms(string cst)
    {
        var r = TaxCalculator.Calculate(Input(Cst(cst), inState: true));
        Assert.Equal(cst, r.IcmsCode);
        Assert.Equal(0m, r.IcmsBase);
        Assert.Equal(0m, r.IcmsRate);
        Assert.Equal(0m, r.IcmsValue);
    }

    [Fact]
    public void Out_state_ignores_any_rate_and_uses_the_automatic_one()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00", rate: 18m), customerState: "PR"));
        Assert.Equal(12m, r.IcmsRate);
        Assert.Equal(7200.00m, r.IcmsValue);
    }

    [Fact]
    public void Imported_goods_get_four_percent_interstate()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), origin: 1));
        Assert.Equal(4m, r.IcmsRate);
        Assert.Equal(2400.00m, r.IcmsValue);
    }

    [Fact]
    public void Simples_nacional_uses_the_csosn_of_the_block()
    {
        var icms = new IcmsRule("00", "102", null, null, null, null);
        var r = TaxCalculator.Calculate(Input(icms, regime: TaxRegime.SimplesNacional));

        Assert.Equal("102", r.IcmsCode);
        Assert.Equal(0m, r.IcmsValue);
    }

    [Fact]
    public void Mei_uses_csosn_and_csosn_900_taxes_like_cst_90()
    {
        var icms = new IcmsRule(null, "900", 18m, 10m, null, null);
        var r = TaxCalculator.Calculate(Input(icms, inState: true, regime: TaxRegime.Mei, amount: 1000m));

        Assert.Equal("900", r.IcmsCode);
        Assert.Equal(900.00m, r.IcmsBase);
        Assert.Equal(162.00m, r.IcmsValue);
    }

    [Fact]
    public void Excess_sublimit_regime_uses_cst()
    {
        var icms = new IcmsRule("00", "102", 18m, null, null, null);
        var r = TaxCalculator.Calculate(Input(icms, inState: true, regime: TaxRegime.SimplesNacionalExcess, amount: 100m));
        Assert.Equal("00", r.IcmsCode);
        Assert.Equal(18.00m, r.IcmsValue);
    }

    [Fact]
    public void Benefit_code_is_copied() =>
        Assert.Equal("SP800001",
            TaxCalculator.Calculate(Input(new IcmsRule("40", null, null, null, null, "SP800001"), inState: true))
                .IcmsBenefitCode);

    [Fact]
    public void Without_tema_69_the_pis_base_is_the_full_amount()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), pisCofins: new("01", 1.65m, "01", 7.6m, false)));
        Assert.Equal(60000.00m, r.PisBase);
        Assert.Equal(990.00m, r.PisValue);
        Assert.Equal(4560.00m, r.CofinsValue);
    }

    [Theory]
    [InlineData("04")]
    [InlineData("06")]
    [InlineData("09")]
    public void No_tax_pis_cofins_cst_zeroes_base_and_value(string cst)
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), pisCofins: new(cst, 1.65m, cst, 7.6m, true)));
        Assert.Equal(0m, r.PisBase);
        Assert.Equal(0m, r.PisRate);
        Assert.Equal(0m, r.PisValue);
        Assert.Equal(0m, r.CofinsBase);
        Assert.Equal(0m, r.CofinsValue);
    }

    [Fact]
    public void Ibs_cbs_reductions_apply_to_their_own_tax()
    {
        var r = TaxCalculator.Calculate(Input(
            Cst("40"), inState: true, amount: 1000m,
            pisCofins: new("09", null, "09", null, false),
            ibsCbs: new IbsCbsRule("200", "200001", IbsReduction: 60m, CbsReduction: 40m),
            rates: new IbsCbsRates(10m, 5m, 5m)));

        Assert.Equal(1000.00m, r.IbsCbsBase);
        Assert.Equal(60.00m, r.CbsValue);        // 1000 × 10% × (1 − 40%)
        Assert.Equal(20.00m, r.IbsStateValue);   // 1000 × 5% × (1 − 60%)
        Assert.Equal(20.00m, r.IbsMunicipalValue);
        Assert.Equal(40m, r.CbsRateReduction);
        Assert.Equal(60m, r.IbsRateReduction);
    }

    [Theory]
    [InlineData("400")]
    [InlineData("410")]
    public void No_tax_ibs_cbs_cst_zeroes_everything(string cst)
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), ibsCbs: new IbsCbsRule(cst, "410001", null, null), rates: Rates2026));
        Assert.Equal(cst, r.IbsCbsCst);
        Assert.Equal("410001", r.IbsCbsClassCode);
        Assert.Equal(0m, r.IbsCbsBase);
        Assert.Equal(0m, r.CbsRate);
        Assert.Equal(0m, r.CbsValue);
        Assert.Equal(0m, r.IbsStateValue);
    }

    [Fact]
    public void Without_ibs_cbs_rule_the_group_is_empty()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00")));
        Assert.Null(r.IbsCbsCst);
        Assert.Null(r.IbsCbsClassCode);
        Assert.Equal(0m, r.IbsCbsBase);
        Assert.Equal(0m, r.CbsValue);
    }

    [Fact]
    public void Rounding_is_half_away_from_zero()
    {
        // Meio centavo exato: 0,25 × 18% = 0,045 → 0,05 (o arredondamento bancário daria 0,04).
        var r = TaxCalculator.Calculate(Input(Cst("00", rate: 18m), inState: true, amount: 0.25m));
        Assert.Equal(0.05m, r.IcmsValue);
    }

    /// <summary>Compra interestadual: a alíquota segue a mercadoria (BA → SP = 12%), não a filial (SP → BA = 7%).</summary>
    [Fact]
    public void Interstate_entry_uses_the_supplier_state_as_origin()
    {
        var r = TaxCalculator.Calculate(new TaxCalculationInput(
            Amount: 60000m, InState: false, OriginState: "BA", DestinationState: "SP", Regime: TaxRegime.Normal,
            GoodsOrigin: 0, Icms: Cst("00"), PisCofins: PisCofins, IbsCbs: null, Rates: null));

        Assert.Equal(12m, r.IcmsRate);
    }
}
