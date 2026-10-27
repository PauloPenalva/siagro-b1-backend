using System.Text.Json;
using System.Text.Json.Serialization;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Detection;

public sealed record InstalledService(ServiceKind Kind, string ServiceName, string ExePath, string Environment, int Port);

/// <summary>&lt;base&gt;\install.json — o que o assistente instalou nesta pasta.</summary>
public sealed record InstallManifest(
    string Version,
    string Instance,
    string BasePath,
    string Environment,
    IReadOnlyList<InstalledService> Services,
    DateTime InstalledAt)
{
    public const string FileName = "install.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static InstallManifest? Load(string basePath)
    {
        var path = Path.Combine(basePath, FileName);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            // Manifesto corrompido: cai na detecção pelos serviços, que é a fonte da verdade.
            return null;
        }
    }

    public void Save(string basePath)
    {
        Directory.CreateDirectory(basePath);
        File.WriteAllText(Path.Combine(basePath, FileName), JsonSerializer.Serialize(this, Options));
    }
}
