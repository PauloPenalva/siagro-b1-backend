namespace SiagroB1.Commons.Validation;

/// <summary>
/// Validação de CPF (11 dígitos) e CNPJ (14 caracteres) pelos dígitos verificadores. Aceita o
/// CNPJ alfanumérico da Receita Federal (emitido desde julho de 2026): as 12 primeiras posições
/// são [0-9A-Z] e as 2 últimas, dígitos; no cálculo cada caractere vale o código ASCII menos 48
/// (o que mantém o valor dos dígitos e faz 'A' = 17 ... 'Z' = 42).
/// </summary>
public static class TaxIdValidator
{
    /// <summary>CPF/CNPJ em maiúsculas, sem pontuação: só [0-9A-Z]. É a forma gravada.</summary>
    public static string Normalize(string? value) =>
        new string((value ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    public static bool IsValid(string? value)
    {
        var v = Normalize(value);
        if (v.Length == 0 || v.Distinct().Count() == 1) return false;
        return v.Length switch
        {
            11 => v.All(char.IsAsciiDigit) &&
                  Check(v, 9, [10, 9, 8, 7, 6, 5, 4, 3, 2]) && Check(v, 10, [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]),
            14 => char.IsAsciiDigit(v[12]) && char.IsAsciiDigit(v[13]) &&
                  Check(v, 12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) && Check(v, 13, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]),
            _ => false,
        };
    }

    private static bool Check(string value, int position, int[] weights)
    {
        var sum = weights.Select((w, i) => (value[i] - '0') * w).Sum();
        var rest = sum % 11;
        var expected = rest < 2 ? 0 : 11 - rest;
        return value[position] - '0' == expected;
    }
}
