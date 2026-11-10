using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Client;

/// <summary>Nomes fixos da instalação do SiagroB1.Client (leitor da balança rodoviária).</summary>
public static class ClientInstall
{
    public const string ServiceName = "siagrob1-client";
    public const string DisplayName = "SiagroB1 - Leitor da Balança";
    public const string Description = "Lê o indicador da balança rodoviária por TCP e envia o peso ao SiagroB1.";
    public const string ExeName = "SiagroB1.Client.exe";
    public const string DefaultDirectory = @"C:\IdxSistemas\SiagroB1\Client";
}

/// <summary>Valores digitados no assistente. <see cref="Validate"/> devolve as mensagens (pt-BR) de cada problema.</summary>
public sealed record ClientInstallSettings(string ScaleCodes, string WebSocketUrl, string ClientKey, string InstallDirectory)
{
    public IReadOnlyList<string> Codes =>
        ScaleCodes.Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Codes.Count == 0)
        {
            errors.Add("Informe o código de pelo menos uma balança (ex.: TS01).");
        }

        var url = WebSocketUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
        {
            errors.Add("A URL do servidor deve começar com ws:// ou wss:// (ex.: ws://servidor:55000/ws/truck-scale).");
        }
        else if (url.EndsWith('/') || uri.Query.Length > 0)
        {
            errors.Add("A URL do servidor não pode terminar com barra nem ter parâmetros (?...).");
        }

        if (string.IsNullOrWhiteSpace(ClientKey))
        {
            errors.Add("Informe a chave da balança: a mesma TruckScale:ClientKey do servidor. Sem ela qualquer um injeta peso.");
        }

        if (string.IsNullOrWhiteSpace(InstallDirectory) || !Path.IsPathFullyQualified(InstallDirectory))
        {
            errors.Add("Informe a pasta de instalação completa (ex.: C:\\IdxSistemas\\SiagroB1\\Client).");
        }

        return errors;
    }

    /// <summary>appsettings.json do Client. Sempre <c>UseMockScale=false</c>: o peso simulado é só de desenvolvimento.</summary>
    public JsonObject ToAppSettings() => new()
    {
        ["Logging"] = new JsonObject
        {
            ["LogLevel"] = new JsonObject
            {
                ["Default"] = "Information",
                ["Microsoft.Hosting.Lifetime"] = "Information"
            }
        },
        ["TruckScaleIds"] = new JsonArray(Codes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
        ["WebSocketUrl"] = WebSocketUrl.Trim(),
        ["UseMockScale"] = false,
        ["TruckScale"] = new JsonObject { ["ClientKey"] = ClientKey.Trim() }
    };

    /// <summary>Lê um appsettings.json já instalado para pré-preencher o assistente numa atualização.</summary>
    public static ClientInstallSettings? FromInstalled(string installDirectory)
    {
        var path = Path.Combine(installDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return null;
        }
        var json = JsonFile.Load(path);
        var codes = json["TruckScaleIds"] is JsonArray array
            ? string.Join(", ", array.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s)))
            : "";
        return new ClientInstallSettings(
            codes,
            json["WebSocketUrl"]?.GetValue<string>() ?? "",
            json["TruckScale"]?["ClientKey"]?.GetValue<string>() ?? "",
            installDirectory);
    }

    public void Write() => JsonFile.Save(Path.Combine(InstallDirectory, "appsettings.json"), ToAppSettings());
}
