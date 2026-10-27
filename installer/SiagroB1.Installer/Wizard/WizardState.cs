using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Wizard;

public sealed record PayloadInfo(string Dir, string Version);

/// <summary>Dependências externas das páginas, trocáveis nos testes.</summary>
public sealed record WizardServices(
    Func<string, CancellationToken, Task<string?>> TestConnection,
    Func<int, bool> IsPortInUse,
    IServiceQuery ServiceQuery,
    Func<IServiceManager> ServiceManager,
    Func<MigrationRunner> Migrations);

public sealed class WizardState(PayloadInfo payload)
{
    public const string DefaultBasePath = @"C:\IdxSistemas\SiagroB1\Producao";

    public PayloadInfo Payload { get; } = payload;
    public InstallSettings Settings { get; set; } = new();
    public string BasePath { get; set; } = DefaultBasePath;
    public string Instance { get; set; } = "Producao";
    public string Environment { get; set; } = "Production";
    public DetectionResult Detection { get; set; } = new(null, [], false);

    /// <summary>appsettings.&lt;Ambiente&gt;.json atual de cada serviço (null = não existe).</summary>
    public Dictionary<ServiceKind, JsonObject?> ClientFiles { get; } = new()
    {
        [ServiceKind.Web] = null,
        [ServiceKind.Gateway] = null,
        [ServiceKind.Reports] = null
    };

    public MigrationReport? MigrationReport { get; set; }
    public bool DatabaseBackupConfirmed { get; set; }
    public string? DdlUser { get; set; }
    public string? DdlPassword { get; set; }

    public bool IsUpdate => Detection.IsInstalled;

    public JsonObject? PayloadDefaults(ServiceKind kind)
    {
        var path = Path.Combine(Payload.Dir, kind.ToString(), "appsettings.json");
        return File.Exists(path) ? JsonFile.Load(path) : null;
    }
}
