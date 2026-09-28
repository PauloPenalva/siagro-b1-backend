using System.Text.RegularExpressions;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Substitui <c>{{nome}}</c> pelo valor do dicionário. Função pura, sem banco, testável sozinha.
/// Placeholder desconhecido é erro de negócio com a lista completa: contrato com <c>{{x}}</c> no
/// meio não pode sair silenciosamente. A mesma checagem roda ao salvar o modelo, para o erro
/// aparecer na edição e não na hora de gerar a minuta.
/// </summary>
public static partial class ContractDraftTemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*([a-z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    public static string Render(string html, IReadOnlyDictionary<string, string> values)
    {
        var unknown = FindUnknown(html, values.Keys.ToHashSet(StringComparer.Ordinal));
        if (unknown.Count > 0)
            throw new BusinessException(UnknownPlaceholdersMessage(unknown));

        return Placeholder().Replace(html, m => values[m.Groups[1].Value]);
    }

    /// <summary>Nomes usados no HTML que não estão em <paramref name="known"/>, sem repetição, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> FindUnknown(string html, IReadOnlySet<string> known)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();

        foreach (Match m in Placeholder().Matches(html))
        {
            var name = m.Groups[1].Value;
            if (!known.Contains(name) && seen.Add(name))
                unknown.Add(name);
        }

        return unknown;
    }

    public static string UnknownPlaceholdersMessage(IEnumerable<string> names) =>
        $"Campos não reconhecidos no modelo: {string.Join(", ", names)}. " +
        "Use apenas os campos da lista de placeholders disponíveis.";
}
