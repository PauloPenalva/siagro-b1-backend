using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>
/// A tela da natureza valida COERÊNCIA, nunca exigência: natureza sem tributação nenhuma é
/// válida (a MH Agro, STANDALONE sem NF-e, não pode ser obrigada a preencher).
/// </summary>
public class UsageTaxationValidatorTests
{
    private static UsageModel Empty(UsageDirection direction = UsageDirection.Outgoing) =>
        new() { Name = "N", Direction = direction };

    private static void Invalid(UsageModel model, string expected)
    {
        var ex = Assert.Throws<DefaultException>(() => UsageTaxationValidator.Validate(model));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Usage_without_any_taxation_is_valid() =>
        UsageTaxationValidator.Validate(Empty());

    [Theory]
    [InlineData("6102")]
    [InlineData("1102")]
    [InlineData("510")]
    public void Outgoing_in_state_cfop_must_start_with_5(string cfop)
    {
        var m = Empty();
        m.CfopOutgoingInState = cfop;
        Invalid(m, "CFOP");
    }

    [Fact]
    public void Outgoing_out_state_cfop_must_start_with_6()
    {
        var m = Empty();
        m.CfopOutgoingOutState = "5102";
        Invalid(m, "CFOP");
    }

    [Fact]
    public void Incoming_cfops_must_start_with_1_and_2()
    {
        var m = Empty(UsageDirection.Incoming);
        m.CfopIncomingInState = "1102";
        m.CfopIncomingOutState = "2102";
        UsageTaxationValidator.Validate(m);

        m.CfopIncomingOutState = "6102";
        Invalid(m, "CFOP");
    }

    [Fact]
    public void Unknown_icms_cst_is_rejected()
    {
        var m = Empty();
        m.IcmsInStateCst = "10";
        Invalid(m, "CST de ICMS");
    }

    [Fact]
    public void Unknown_csosn_is_rejected()
    {
        var m = Empty();
        m.IcmsOutStateCsosn = "101";
        Invalid(m, "CSOSN");
    }

    [Fact]
    public void Taxed_in_state_cst_requires_a_rate()
    {
        var m = Empty();
        m.IcmsInStateCst = "00";
        Invalid(m, "alíquota");

        m.IcmsInStateRate = 18m;
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Taxed_out_state_cst_does_not_need_a_rate()
    {
        var m = Empty();
        m.IcmsOutStateCst = "00";
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Cst_20_requires_base_reduction()
    {
        var m = Empty();
        m.IcmsInStateCst = "20";
        m.IcmsInStateRate = 18m;
        Invalid(m, "redução");

        m.IcmsInStateBaseReduction = 33.33m;
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Cst_51_requires_deferral_and_deferral_requires_cst_51()
    {
        var m = Empty();
        m.IcmsInStateCst = "51";
        m.IcmsInStateRate = 18m;
        Invalid(m, "diferimento");

        m.IcmsInStateDeferral = 100m;
        UsageTaxationValidator.Validate(m);

        m.IcmsInStateCst = "00";
        Invalid(m, "diferimento");
    }

    [Fact]
    public void Exempt_cst_rejects_rate_reduction_and_deferral()
    {
        var m = Empty();
        m.IcmsInStateCst = "40";
        m.IcmsInStateRate = 18m;
        Invalid(m, "não aceita");
    }

    [Fact]
    public void Exempt_csosn_rejects_rate()
    {
        var m = Empty();
        m.IcmsInStateCsosn = "102";
        m.IcmsInStateRate = 18m;
        Invalid(m, "não aceita");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Percentages_must_be_between_0_and_100(double value)
    {
        var m = Empty();
        m.PisCst = "01";
        m.PisRate = (decimal)value;
        Invalid(m, "entre 0 e 100");
    }

    /// <summary>
    /// CST 03 é alíquota POR UNIDADE (R$/unidade), que o cálculo deste sub-projeto não faz —
    /// aceitá-lo daria PIS/COFINS errado numa linha travada.
    /// </summary>
    [Fact]
    public void Pis_cofins_cst_03_is_not_supported()
    {
        var m = Empty();
        m.PisCst = "03";
        Invalid(m, "CST de PIS");
    }

    /// <summary>CST 01/02 tributam: sem alíquota a linha sairia com PIS/COFINS zero em silêncio.</summary>
    [Theory]
    [InlineData("01")]
    [InlineData("02")]
    public void Taxed_pis_cofins_cst_requires_a_rate(string cst)
    {
        var m = Empty();
        m.PisCst = cst;
        Invalid(m, "alíquota de PIS");

        m.PisRate = 1.65m;
        m.CofinsCst = cst;
        Invalid(m, "alíquota de COFINS");

        m.CofinsRate = 7.6m;
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Pis_cst_must_match_the_direction()
    {
        var m = Empty();
        m.PisCst = "50";
        Invalid(m, "CST de PIS");

        var incoming = Empty(UsageDirection.Incoming);
        incoming.CofinsCst = "01";
        Invalid(incoming, "CST de COFINS");
    }

    [Fact]
    public void Ibs_cbs_cst_requires_six_digit_class_code()
    {
        var m = Empty();
        m.IbsCbsCst = "000";
        Invalid(m, "cClassTrib");

        m.IbsCbsClassCode = "12345";
        Invalid(m, "cClassTrib");

        m.IbsCbsClassCode = "000001";
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Ibs_cbs_cst_200_requires_a_reduction_and_410_rejects_it()
    {
        var m = Empty();
        m.IbsCbsCst = "200";
        m.IbsCbsClassCode = "200001";
        Invalid(m, "redução");

        m.CbsRateReduction = 60m;
        UsageTaxationValidator.Validate(m);

        m.IbsCbsCst = "410";
        Invalid(m, "não aceita");
    }

    [Fact]
    public void Unknown_ibs_cbs_cst_is_rejected()
    {
        var m = Empty();
        m.IbsCbsCst = "510";
        m.IbsCbsClassCode = "510001";
        Invalid(m, "CST de IBS/CBS");
    }
}
