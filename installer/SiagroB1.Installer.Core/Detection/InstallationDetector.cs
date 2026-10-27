using System.Runtime.Versioning;
using Microsoft.Win32;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Detection;

public interface IServiceQuery
{
    IEnumerable<(string Name, string BinPath)> All();
}

/// <summary>Lê o ImagePath de todos os serviços no registro (sem dependência de WMI).</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryServiceQuery : IServiceQuery
{
    public IEnumerable<(string Name, string BinPath)> All()
    {
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null)
        {
            yield break;
        }
        foreach (var name in services.GetSubKeyNames())
        {
            using var key = services.OpenSubKey(name);
            if (key?.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string path)
            {
                yield return (name, path);
            }
        }
    }
}

public sealed record DetectionResult(InstallManifest? Manifest, IReadOnlyList<InstalledService> Services, bool IsLegacy)
{
    public bool IsInstalled => Services.Count > 0;
}

public sealed class InstallationDetector(IServiceQuery query)
{
    private static readonly Dictionary<string, ServiceKind> ExeKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SiagroB1.Web.exe"] = ServiceKind.Web,
        ["SiagroB1.Gateway.exe"] = ServiceKind.Gateway,
        ["SiagroB1.Reports.exe"] = ServiceKind.Reports
    };

    public DetectionResult Detect(string basePath)
    {
        var manifest = InstallManifest.Load(basePath);
        if (manifest is not null)
        {
            return new DetectionResult(manifest, manifest.Services, false);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath)) + Path.DirectorySeparatorChar;
        var found = new List<InstalledService>();
        foreach (var (name, binPath) in query.All())
        {
            if (BinPathParser.Parse(binPath) is not { } parsed
                || !ExeKinds.TryGetValue(Path.GetFileName(parsed.ExePath), out var kind)
                || !Path.GetFullPath(parsed.ExePath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            found.Add(new InstalledService(kind, name, parsed.ExePath, parsed.Environment, parsed.Port));
        }

        return new DetectionResult(null, found, found.Count > 0);
    }
}
