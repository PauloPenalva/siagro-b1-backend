using System.Globalization;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Fiscal.Taxes;

/// <summary>Alíquotas (%) da contribuição social do produtor rural que a linha retém.</summary>
public readonly record struct RuralContributionRates(decimal Inss, decimal Rat, decimal Senar);

/// <summary>
/// Regra da linha da entrada (spec 2026-10-18 §4.4): a natureza diz o modo, o cadastro do parceiro diz se é produtor
/// rural e o enquadramento, e a tabela vigente dá as alíquotas. A retenção só existe quando natureza e parceiro
/// concordam; PJ recolhe por conta própria e quem não está marcado como produtor não retém — sem erro.
/// </summary>
public static class RuralContributionRule
{
    /// <summary>null = a linha não retém (Clear). Lança DefaultException só quando falta a vigência.</summary>
    public static RuralContributionRates? Resolve(
        RuralContributionMode mode, bool isRuralProducer, RuralProducerType? producerType, bool payrollOption,
        RuralContributionRates? standard, RuralContributionRates? payroll, DateOnly rateDate)
    {
        if (!Withholds(mode, isRuralProducer, producerType))
            return null;

        if (mode == RuralContributionMode.SenarOnly)
            return new RuralContributionRates(0m, 0m, Required(standard, rateDate).Senar);

        return Required(payrollOption ? payroll : standard, rateDate);
    }

    /// <summary>Qual vigência a linha consulta; null quando o modo não retém (o serviço não lê a tabela à toa).</summary>
    public static RuralContributionScheme? SchemeFor(RuralContributionMode mode, bool payrollOption) => mode switch
    {
        RuralContributionMode.Full => payrollOption ? RuralContributionScheme.PayrollOption : RuralContributionScheme.Standard,
        // A opção pela folha não muda o SENAR, que é o do regime de comercialização.
        RuralContributionMode.SenarOnly => RuralContributionScheme.Standard,
        _ => null,
    };

    /// <summary>Natureza que retém e parceiro produtor pessoa física ou segurado especial.</summary>
    public static bool Withholds(RuralContributionMode mode, bool isRuralProducer, RuralProducerType? producerType) =>
        mode is RuralContributionMode.Full or RuralContributionMode.SenarOnly
        && isRuralProducer
        && producerType is RuralProducerType.Individual or RuralProducerType.SpecialInsured;

    private static RuralContributionRates Required(RuralContributionRates? rates, DateOnly rateDate) =>
        rates ?? throw new DefaultException(
            $"Cadastre as alíquotas de contribuição social vigentes em {rateDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.");
}
