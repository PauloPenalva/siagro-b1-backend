namespace SiagroB1.Commons.Validation;

/// <summary>Validação de CPF (11 dígitos) e CNPJ (14 dígitos) pelos dígitos verificadores.</summary>
public static class TaxIdValidator
{
    public static string Digits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    public static bool IsValid(string? value)
    {
        var d = Digits(value);
        if (d.Distinct().Count() == 1) return false;
        return d.Length switch
        {
            11 => Check(d, 9, [10, 9, 8, 7, 6, 5, 4, 3, 2]) && Check(d, 10, [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]),
            14 => Check(d, 12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) && Check(d, 13, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]),
            _ => false,
        };
    }

    private static bool Check(string digits, int position, int[] weights)
    {
        var sum = weights.Select((w, i) => (digits[i] - '0') * w).Sum();
        var rest = sum % 11;
        var expected = rest < 2 ? 0 : 11 - rest;
        return digits[position] - '0' == expected;
    }
}
