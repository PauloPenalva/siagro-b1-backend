using System.Text;
using System.Text.RegularExpressions;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Texto da CC-e (xCorrecao). O XSD aceita só Latin-1 visível com espaços simples no meio
/// (<c>[!-ÿ]{1}[ -ÿ]{0,}[!-ÿ]{1}</c>) e 15 a 1000 caracteres. Texto colado do Word traz aspas curvas,
/// travessão e quebras de linha: viram ASCII e espaço. O que sobrar fora do Latin-1 é recusado pela Application,
/// antes de chegar à validação da Zeus (que viraria um "sem resposta" genérico).
/// </summary>
public static partial class NfeCorrectionText
{
    public const int MinLength = 15;
    public const int MaxLength = 1000;

    private static readonly Dictionary<char, string> Typographic = new()
    {
        ['‘'] = "'", ['’'] = "'", ['“'] = "\"", ['”'] = "\"",
        ['–'] = "-", ['—'] = "-", ['…'] = "...",
    };

    public static string Normalize(string? value)
    {
        var text = new StringBuilder();
        foreach (var c in value ?? string.Empty)
        {
            if (Typographic.TryGetValue(c, out var replacement))
                text.Append(replacement);
            else
                text.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        return RepeatedSpaces().Replace(text.ToString(), " ").Trim();
    }

    /// <summary>Caracteres que o XSD recusa (fora de U+0020–U+00FF), um de cada, na ordem em que aparecem.</summary>
    public static IReadOnlyList<char> InvalidCharacters(string normalized) =>
        normalized.Where(c => c is < ' ' or > 'ÿ').Distinct().ToList();

    [GeneratedRegex(" {2,}")]
    private static partial Regex RepeatedSpaces();
}
