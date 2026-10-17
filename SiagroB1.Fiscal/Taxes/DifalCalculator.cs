using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Taxes;

/// <summary>
/// Entrada do DIFAL de uma linha. <c>OperationValue</c> é o total da linha (já com frete, seguro, outras despesas
/// e desconto); alíquotas em percentual (18 = 18 %). <c>OriginIcmsValue</c> zero ou negativo = não informado.
/// </summary>
public sealed record DifalInput(
    decimal OperationValue, decimal InterstateRate, decimal OriginIcmsValue,
    decimal InternalRate, decimal FcpRate, DifalBaseRule BaseRule);

/// <summary>DIFAL e FCP da linha, já arredondados.</summary>
public sealed record DifalResult(
    decimal Base, decimal InternalRate, decimal InterstateRate, decimal Value, decimal FcpRate, decimal FcpValue)
{
    /// <summary>Linha sem DIFAL: tudo zero.</summary>
    public static readonly DifalResult None = new(0m, 0m, 0m, 0m, 0m, 0m);
}

/// <summary>
/// DIFAL (EC 87/2015) e FCP (ADCT, art. 82) de uma linha de compra interestadual (spec 2026-10-17 §2).
/// Base única: base = valor da operação. Base dupla: base = (valor - ICMS de origem) ÷ (1 - (interna + FCP)).
/// DIFAL = base × interna - ICMS de origem, nunca negativo; interestadual >= interna zera o DIFAL, mas o FCP
/// continua incidindo sobre a base. A base é arredondada primeiro e os valores saem da base arredondada.
/// </summary>
public static class DifalCalculator
{
    public static DifalResult Calculate(DifalInput input)
    {
        var originIcms = input.OriginIcmsValue > 0m
            ? input.OriginIcmsValue
            : input.OperationValue * input.InterstateRate / 100m;

        var baseValue = input.BaseRule == DifalBaseRule.Double
            ? Round((input.OperationValue - originIcms) / DoubleBaseDivisor(input))
            : Round(input.OperationValue);

        var value = input.InterstateRate >= input.InternalRate
            ? 0m
            : Math.Max(0m, Round(baseValue * input.InternalRate / 100m - originIcms));

        var fcpValue = Round(baseValue * input.FcpRate / 100m);

        return new DifalResult(baseValue, input.InternalRate, input.InterstateRate, value, input.FcpRate, fcpValue);
    }

    private static decimal DoubleBaseDivisor(DifalInput input)
    {
        var divisor = 1m - (input.InternalRate + input.FcpRate) / 100m;
        if (divisor <= 0m)
            throw new ArgumentException("Alíquota interna somada ao FCP deve ser menor que 100%.", nameof(input));
        return divisor;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}
