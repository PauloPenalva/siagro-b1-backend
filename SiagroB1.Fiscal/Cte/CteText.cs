using System.Text;
using System.Text.RegularExpressions;

namespace SiagroB1.Fiscal.Cte;

/// <summary>
/// Higieniza texto livre antes de ir ao XML do CT-e. O XSD (TString) proíbe espaço no início/fim
/// e espaços duplos, e não aceita caracteres de controle: um cadastro com espaço sobrando
/// derrubaria a validação local. Limpa na fronteira de entrada do XML, sem alterar o cadastro.
/// </summary>
public static partial class CteText
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Remove controles, colapsa espaços, apara e corta em <paramref name="maxLength"/>; vazio vira "".</summary>
    public static string Clean(string? value, int maxLength = int.MaxValue)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(char.IsWhiteSpace(c) ? ' ' : char.IsControl(c) ? '\0' : c);

        var cleaned = Whitespace().Replace(sb.ToString().Replace("\0", string.Empty), " ").Trim();
        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength].TrimEnd();

        return cleaned;
    }

    /// <summary>Como <see cref="Clean"/>, mas devolve <c>null</c> quando sobra vazio (campo opcional é omitido).</summary>
    public static string? CleanOrNull(string? value, int maxLength = int.MaxValue)
    {
        var cleaned = Clean(value, maxLength);
        return cleaned.Length == 0 ? null : cleaned;
    }
}
