using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Taxes;

namespace SiagroB1.Fiscal.Tests.Taxes;

/// <summary>
/// Regra da linha (spec 2026-10-18 §4.4): a retenção existe só quando a natureza e o cadastro do parceiro concordam;
/// as alíquotas vêm da tabela vigente do regime.
/// </summary>
public class RuralContributionRuleTests
{
    private static readonly RuralContributionRates Standard = new(1.32m, 0.11m, 0.20m);
    private static readonly RuralContributionRates Payroll = new(0m, 0m, 0.20m);
    private static readonly DateOnly RateDate = new(2026, 10, 1);

    public static TheoryData<RuralContributionMode, bool, RuralProducerType?, bool, RuralContributionRates?> Cases => new()
    {
        { RuralContributionMode.Full, true, RuralProducerType.Individual, false, Standard },
        { RuralContributionMode.Full, true, RuralProducerType.SpecialInsured, false, Standard },
        { RuralContributionMode.Full, true, RuralProducerType.Individual, true, new RuralContributionRates(0m, 0m, 0.20m) },
        { RuralContributionMode.SenarOnly, true, RuralProducerType.Individual, true, new RuralContributionRates(0m, 0m, 0.20m) },
        { RuralContributionMode.SenarOnly, true, RuralProducerType.SpecialInsured, false, new RuralContributionRates(0m, 0m, 0.20m) },
        { RuralContributionMode.Full, true, RuralProducerType.LegalEntity, false, null },
        { RuralContributionMode.SenarOnly, true, RuralProducerType.LegalEntity, false, null },
        { RuralContributionMode.Full, false, null, false, null },
        { RuralContributionMode.SenarOnly, false, null, false, null },
        { RuralContributionMode.None, true, RuralProducerType.Individual, false, null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolves_the_rates_of_the_line(
        RuralContributionMode mode, bool isProducer, RuralProducerType? type, bool payrollOption, RuralContributionRates? expected)
    {
        var result = RuralContributionRule.Resolve(mode, isProducer, type, payrollOption, Standard, Payroll, RateDate);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Payroll_producer_without_the_payroll_rates_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => RuralContributionRule.Resolve(
            RuralContributionMode.Full, true, RuralProducerType.Individual, true, Standard, null, RateDate));

        Assert.Equal("Cadastre as alíquotas de contribuição social vigentes em 01/10/2026.", ex.Message);
    }

    [Fact]
    public void Senar_only_without_the_standard_rates_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => RuralContributionRule.Resolve(
            RuralContributionMode.SenarOnly, true, RuralProducerType.Individual, false, null, Payroll, RateDate));

        Assert.Equal("Cadastre as alíquotas de contribuição social vigentes em 01/10/2026.", ex.Message);
    }

    [Fact]
    public void Missing_rates_do_not_matter_when_the_line_is_not_withheld()
    {
        Assert.Null(RuralContributionRule.Resolve(
            RuralContributionMode.Full, true, RuralProducerType.LegalEntity, false, null, null, RateDate));
        Assert.Null(RuralContributionRule.Resolve(
            RuralContributionMode.Full, false, null, false, null, null, RateDate));
    }

    [Theory]
    [InlineData(RuralContributionMode.None, false, null)]
    [InlineData(RuralContributionMode.None, true, null)]
    [InlineData(RuralContributionMode.Full, false, RuralContributionScheme.Standard)]
    [InlineData(RuralContributionMode.Full, true, RuralContributionScheme.PayrollOption)]
    [InlineData(RuralContributionMode.SenarOnly, false, RuralContributionScheme.Standard)]
    [InlineData(RuralContributionMode.SenarOnly, true, RuralContributionScheme.Standard)]
    public void Scheme_says_which_rates_to_load(RuralContributionMode mode, bool payrollOption, RuralContributionScheme? expected) =>
        Assert.Equal(expected, RuralContributionRule.SchemeFor(mode, payrollOption));
}
