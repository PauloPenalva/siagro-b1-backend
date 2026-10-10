using System.Globalization;

namespace SiagroB1.Fiscal.Taxes;

/// <summary>O que o texto lê de cada linha: alíquotas e valores das três parcelas.</summary>
public sealed record FunruralTextLine(
    decimal InssRate, decimal RatRate, decimal SenarRate, decimal InssValue, decimal RatValue, decimal SenarValue);

/// <summary>
/// Texto do Funrural nas Informações Complementares da NF-e (spec 2026-10-09 §6), no lugar do que o operador
/// digitava à mão. Linhas com as mesmas alíquotas somam; cada combinação distinta vira um trecho.
/// </summary>
public static class FunruralText
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string? Build(IEnumerable<FunruralTextLine> lines, bool isReturn)
    {
        var sentences = lines
            .Where(l => l.InssValue + l.RatValue + l.SenarValue > 0m)
            .GroupBy(l => (l.InssRate, l.RatRate, l.SenarRate))
            .Select(g => Describe(g.Key.InssRate, g.Key.RatRate, g.Key.SenarRate,
                g.Sum(l => l.InssValue), g.Sum(l => l.RatValue), g.Sum(l => l.SenarValue)))
            .Select(text => isReturn ? $"Estorno - {text}" : text)
            .ToList();

        return sentences.Count == 0 ? null : string.Join(" | ", sentences);
    }

    private static string Describe(decimal inssRate, decimal ratRate, decimal senarRate, decimal inss, decimal rat, decimal senar)
    {
        // Decisão judicial: só o SENAR é retido, e a frase não fala em Funrural.
        if (inssRate == 0m && ratRate == 0m)
            return $"SENAR {Rate(senarRate)}% = R$ {Money(senar)}";

        var parts = new List<string>();
        if (inssRate > 0m) parts.Add($"INSS {Rate(inssRate)}% R$ {Money(inss)}");
        if (ratRate > 0m) parts.Add($"RAT {Rate(ratRate)}% R$ {Money(rat)}");
        if (senarRate > 0m) parts.Add($"SENAR {Rate(senarRate)}% R$ {Money(senar)}");

        return $"Contribuição Social denominada Funrural {Rate(inssRate + ratRate + senarRate)}% = " +
               $"R$ {Money(inss + rat + senar)} ({string.Join("; ", parts)})";
    }

    /// <summary>Duas casas no mínimo, até quatro (a alíquota é DECIMAL(7,4)).</summary>
    private static string Rate(decimal value) => value.ToString("0.00##", PtBr);

    private static string Money(decimal value) => value.ToString("N2", PtBr);
}
