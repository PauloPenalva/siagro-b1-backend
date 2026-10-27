using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;
using SiagroB1.Installer.Wizard;

namespace SiagroB1.Installer.Payload;

/// <summary>
/// Acha o pacote da versão: pasta passada por --payload (desenvolvimento), payload.zip ao lado do exe,
/// ou o payload.zip embutido no próprio exe. Extrai para uma pasta temporária e lê manifest.json.
/// </summary>
public static class PayloadExtractor
{
    public const string ResourceName = "payload.zip";

    public static PayloadInfo? Locate(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--payload", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length && Directory.Exists(args[index + 1]))
        {
            return Read(args[index + 1]);
        }

        var target = Path.Combine(Path.GetTempPath(), "SiagroB1Setup", Guid.NewGuid().ToString("N"));
        var beside = Path.Combine(AppContext.BaseDirectory, ResourceName);
        if (File.Exists(beside))
        {
            ZipFile.ExtractToDirectory(beside, target);
            return Read(target);
        }

        using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (embedded is null)
        {
            return null;
        }
        ZipFile.ExtractToDirectory(embedded, target);
        return Read(target);
    }

    private static PayloadInfo Read(string dir)
    {
        var manifest = Path.Combine(dir, "manifest.json");
        var version = File.Exists(manifest)
            ? JsonNode.Parse(File.ReadAllText(manifest))?["version"]?.GetValue<string>() ?? "?"
            : "desenvolvimento";
        return new PayloadInfo(dir, version);
    }
}
