using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Execution;

public interface IInstallStep
{
    string Title { get; }
    Task ExecuteAsync(InstallContext ctx, CancellationToken ct);
    Task RollbackAsync(InstallContext ctx, CancellationToken ct);
}

/// <summary>Estado compartilhado entre os passos de uma execução do assistente.</summary>
public sealed class InstallContext(
    InstallSettings settings,
    DetectionResult detection,
    string basePath,
    string instance,
    string environment,
    string payloadDir,
    string version,
    IProgress<string> log)
{
    public InstallSettings Settings { get; } = settings;
    public DetectionResult Detection { get; } = detection;
    public string BasePath { get; } = basePath;
    public string Instance { get; } = instance;
    public string Environment { get; } = environment;
    public string PayloadDir { get; } = payloadDir;
    public string Version { get; } = version;
    public IProgress<string> Log { get; } = log;

    public IReadOnlyList<InstalledService> TargetServices { get; set; } = [];
    public string? BackupDir { get; set; }
    public MigrationReport? MigrationReport { get; set; }
    public bool DatabaseBackupConfirmed { get; set; }
    public string? DdlUser { get; set; }
    public string? DdlPassword { get; set; }
    public List<string> Warnings { get; } = [];

    public bool IsUpdate => Detection.IsInstalled;

    public string ServiceDir(InstalledService service) => Path.GetDirectoryName(service.ExePath)!;
    public string PayloadDirFor(ServiceKind kind) => Path.Combine(PayloadDir, kind.ToString());
    public string MigrationsDir => Path.Combine(PayloadDir, "migrations");
}
