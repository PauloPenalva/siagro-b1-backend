using System.Text.Json.Nodes;

namespace SiagroB1.Installer.Core.Settings;

public sealed record MergeResult(
    JsonObject Result,
    IReadOnlyList<string> NewKeys,
    IReadOnlyList<string> ObsoleteKeys,
    IReadOnlyList<string> ChangedKeys);

/// <summary>
/// Monta o novo appsettings.&lt;Ambiente&gt;.json do cliente a partir do arquivo atual dele e do que o
/// usuário alterou no assistente. O padrão da versão NÃO é copiado: ele continua valendo em runtime
/// pela cascata de configuração; só serve para apontar chaves novas e obsoletas.
/// Chaves comparadas sem diferenciar maiúsculas, como faz a configuração do ASP.NET Core.
/// </summary>
public static class AppSettingsMerger
{
    public static MergeResult Merge(
        JsonObject defaults,
        JsonObject? client,
        IReadOnlyDictionary<string, JsonNode?> edits,
        IReadOnlyCollection<string> dropPaths)
    {
        var result = client is null ? new JsonObject() : client.DeepClone().AsObject();

        foreach (var drop in dropPaths)
        {
            Remove(result, Split(drop));
        }

        var newKeys = new List<string>();
        var obsoleteKeys = new List<string>();
        if (client is not null)
        {
            var defaultLeaves = Leaves(defaults);
            var clientLeaves = Leaves(result);
            var clientSet = new HashSet<string>(clientLeaves, StringComparer.OrdinalIgnoreCase);
            var defaultSet = new HashSet<string>(defaultLeaves, StringComparer.OrdinalIgnoreCase);
            var editSet = new HashSet<string>(edits.Keys, StringComparer.OrdinalIgnoreCase);

            newKeys.AddRange(defaultLeaves.Where(k => !clientSet.Contains(k) && !editSet.Contains(k) && !HasAncestorLeaf(k, clientSet)));
            obsoleteKeys.AddRange(clientLeaves.Where(k => !defaultSet.Contains(k) && !HasAncestorLeaf(k, defaultSet)));
        }

        var changed = new List<string>();
        foreach (var (path, value) in edits)
        {
            var segments = Split(path);
            var current = Get(result, segments);
            if (value is null)
            {
                if (current is not null && Remove(result, segments))
                {
                    changed.Add(path);
                }
                continue;
            }

            if (current is not null && JsonNode.DeepEquals(current, value))
            {
                continue;
            }

            Set(result, segments, value.DeepClone());
            changed.Add(path);
        }

        return new MergeResult(result, newKeys, obsoleteKeys, changed);
    }

    private static string[] Split(string path) => path.Split(':');

    private static bool HasAncestorLeaf(string key, HashSet<string> leaves)
    {
        // Ex.: cliente tem "A" como valor e o padrão tem "A:B" — "A" já cobre a chave.
        var idx = key.LastIndexOf(':');
        while (idx > 0)
        {
            if (leaves.Contains(key[..idx]))
            {
                return true;
            }
            idx = key.LastIndexOf(':', idx - 1);
        }
        return false;
    }

    private static List<string> Leaves(JsonObject obj)
    {
        var list = new List<string>();
        Collect(obj, "", list);
        return list;
    }

    private static void Collect(JsonObject obj, string prefix, List<string> list)
    {
        foreach (var (name, node) in obj)
        {
            var path = prefix.Length == 0 ? name : $"{prefix}:{name}";
            if (node is JsonObject child && child.Count > 0)
            {
                Collect(child, path, list);
            }
            else
            {
                list.Add(path);
            }
        }
    }

    private static string? FindKey(JsonObject obj, string name) =>
        obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Get(JsonObject root, string[] segments)
    {
        JsonNode? current = root;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || FindKey(obj, segment) is not { } key)
            {
                return null;
            }
            current = obj[key];
        }
        return current;
    }

    private static void Set(JsonObject root, string[] segments, JsonNode value)
    {
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var key = FindKey(current, segments[i]);
            if (key is null || current[key] is not JsonObject next)
            {
                key ??= segments[i];
                next = new JsonObject();
                current[key] = next;
            }
            current = next;
        }

        var last = FindKey(current, segments[^1]) ?? segments[^1];
        current[last] = value;
    }

    private static bool Remove(JsonObject root, string[] segments)
    {
        var parent = segments.Length == 1 ? root : Get(root, segments[..^1]) as JsonObject;
        if (parent is null || FindKey(parent, segments[^1]) is not { } key)
        {
            return false;
        }
        return parent.Remove(key);
    }
}
