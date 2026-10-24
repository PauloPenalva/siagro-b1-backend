using System.Globalization;

namespace SiagroB1.Fiscal.Reinf;

/// <summary>Formatação de valores conforme o leiaute da EFD-Reinf.</summary>
public static class ReinfFormat
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Valor monetário com vírgula decimal e duas casas, sem separador de milhar.</summary>
    public static string Money(decimal v) => v.ToString("0.00", PtBr);

    /// <summary>Raiz do CNPJ (8 primeiros dígitos).</summary>
    public static string Root(string cnpj)
    {
        var d = Digits(cnpj);
        return d.Length >= 8 ? d[..8] : d;
    }

    public static string Digits(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : new string(s.Where(char.IsDigit).ToArray());
}
