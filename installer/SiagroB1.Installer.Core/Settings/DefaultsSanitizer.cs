using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiagroB1.Installer.Core.Settings;

/// <summary>
/// Limpa o appsettings.json padrão antes de ir para o pacote: o do repositório tem credenciais de
/// desenvolvimento, e um valor que faltasse no cliente cairia num banco de dev. Esvazia connection
/// strings e todo texto cuja chave pareça segredo; não remove chaves.
/// </summary>
public static class DefaultsSanitizer
{
    public static JsonObject Sanitize(JsonObject input)
    {
        var copy = input.DeepClone().AsObject();
        Walk(copy, "");
        return copy;
    }

    private static void Walk(JsonObject obj, string prefix)
    {
        foreach (var name in obj.Select(p => p.Key).ToList())
        {
            var path = prefix.Length == 0 ? name : $"{prefix}:{name}";
            switch (obj[name])
            {
                case JsonObject child:
                    Walk(child, path);
                    break;
                case JsonValue value when value.GetValueKind() == JsonValueKind.String && IsSecret(path):
                    obj[name] = "";
                    break;
            }
        }
    }

    private static bool IsSecret(string path) =>
        !path.StartsWith("Logging:", StringComparison.OrdinalIgnoreCase)
        && (path.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
            || SecretMasker.MaskValue(path, "x") == "***");
}
