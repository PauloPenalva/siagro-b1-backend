namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Normalização dos textos que vão ao XML. ⚠️ O setter <c>CNPJ</c> da Zeus guarda só o PRIMEIRO
/// trecho alfanumérico: "12.345.678/0001-95" viraria "12". Todo CNPJ/CPF passa por aqui antes.
/// </summary>
public static class NfeText
{
    public static string AlphaNumeric(string? value) =>
        new string((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    public static string Digits(string? value) =>
        new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}
