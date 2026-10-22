using System.Text.RegularExpressions;

namespace SiagroB1.Domain.Entities;

/// <summary>Fórmula da visão gerencial: só códigos de linha somados ou subtraídos ("01 - 02 + 05").</summary>
public static partial class ManagementViewFormula
{
    [GeneratedRegex(@"^\s*[A-Za-z0-9.]+(\s*[+-]\s*[A-Za-z0-9.]+)*\s*$")]
    private static partial Regex Shape();

    [GeneratedRegex(@"([+-]?)\s*([A-Za-z0-9.]+)")]
    private static partial Regex Term();

    public static bool IsValid(string? formula) => formula is not null && Shape().IsMatch(formula);

    /// <summary>Termos com sinal (+1/−1). Fórmula inválida devolve vazio.</summary>
    public static IReadOnlyList<(int Sign, string Code)> Parse(string formula) =>
        !IsValid(formula)
            ? []
            : Term().Matches(formula).Select(m => (m.Groups[1].Value == "-" ? -1 : 1, m.Groups[2].Value)).ToList();
}
