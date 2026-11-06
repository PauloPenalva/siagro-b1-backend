using SiagroB1.Infra;

namespace SiagroB1.Security.Services;

/// <summary>
/// Esconde a árvore "market" do menu quando o módulo Mercado não está contratado
/// (<see cref="MarketModule.ConfigKey"/>). A migration do menu vale para todos os bancos; a chave decide a exibição.
/// </summary>
public static class MarketMenuFilter
{
    public const string RootKey = "market";

    /// <summary>Remove o item "market" e todos os seus descendentes (por ParentKey); chaves só parecidas, como "marketing", ficam.</summary>
    public static List<T> Apply<T>(IEnumerable<T> items, Func<T, string> key, Func<T, string?> parentKey, bool enabled)
    {
        var list = items.ToList();
        if (enabled)
            return list;

        var hidden = new HashSet<string>(StringComparer.Ordinal) { RootKey };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var item in list)
            {
                var parent = parentKey(item);
                if (parent != null && hidden.Contains(parent) && hidden.Add(key(item)))
                    grew = true;
            }
        }

        return list.Where(i => !hidden.Contains(key(i))).ToList();
    }
}
