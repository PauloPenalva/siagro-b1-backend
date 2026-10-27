using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Execution.Steps;

internal static class ClientFiles
{
    public const string LogsDir = "logs";
    public const string LogoPath = @"wwwroot\images\logo.png";

    /// <summary>Arquivos do cliente que a cópia da versão nova nunca apaga nem sobrescreve.</summary>
    public static bool IsPreserved(string relativePath, ServiceKind kind)
    {
        var path = relativePath.Replace('/', '\\');
        if (path.StartsWith(LogsDir + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var name = Path.GetFileName(path);
        if (!path.Contains('\\')
            && name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return kind == ServiceKind.Reports && path.Equals(LogoPath, StringComparison.OrdinalIgnoreCase);
    }

    public static void CopyDirectory(string source, string target, Func<string, bool>? skip = null)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (skip?.Invoke(relative) == true)
            {
                continue;
            }
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}

public sealed class BackupStep(Func<DateTime> now) : IInstallStep
{
    public string Title => "Fazer backup da versão atual";

    public Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        var dir = Path.Combine(ctx.BasePath, "backups", now().ToString("yyyyMMdd-HHmmss"));
        foreach (var service in ctx.TargetServices)
        {
            var source = ctx.ServiceDir(service);
            if (!Directory.Exists(source))
            {
                continue;
            }
            ct.ThrowIfCancellationRequested();
            ClientFiles.CopyDirectory(source, Path.Combine(dir, service.Kind.ToString()),
                relative => relative.StartsWith(ClientFiles.LogsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
        ctx.BackupDir = dir;
        ctx.Log.Report($"Backup em {dir}");
        return Task.CompletedTask;
    }

    // O backup fica: é ele que os outros rollbacks usam, e serve de cópia de segurança depois.
    public Task RollbackAsync(InstallContext ctx, CancellationToken ct) => Task.CompletedTask;
}

public sealed class CopyFilesStep : IInstallStep
{
    private readonly List<string> _createdDirs = [];

    public string Title => "Copiar os arquivos da nova versão";

    public Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var service in ctx.TargetServices)
        {
            ct.ThrowIfCancellationRequested();
            var source = ctx.PayloadDirFor(service.Kind);
            var target = ctx.ServiceDir(service);
            if (!Directory.Exists(target))
            {
                _createdDirs.Add(target);
            }
            Directory.CreateDirectory(target);
            ctx.Log.Report($"Copiando {service.Kind} para {target}...");
            Sync(source, target, service.Kind);
        }
        return Task.CompletedTask;
    }

    public Task RollbackAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var service in ctx.TargetServices)
        {
            var target = ctx.ServiceDir(service);
            if (_createdDirs.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
                continue;
            }

            var backup = ctx.BackupDir is null ? null : Path.Combine(ctx.BackupDir, service.Kind.ToString());
            if (backup is null || !Directory.Exists(backup))
            {
                continue;
            }
            ctx.Log.Report($"Restaurando {service.Kind} do backup...");
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(target, file);
                if (!relative.StartsWith(ClientFiles.LogsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
            ClientFiles.CopyDirectory(backup, target);
        }
        return Task.CompletedTask;
    }

    private static void Sync(string source, string target, ServiceKind kind)
    {
        var incoming = new HashSet<string>(
            Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(source, f)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(target, file);
            if (!incoming.Contains(relative) && !ClientFiles.IsPreserved(relative, kind))
            {
                File.Delete(file);
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }

        ClientFiles.CopyDirectory(source, target,
            relative => ClientFiles.IsPreserved(relative, kind) && File.Exists(Path.Combine(target, relative)));
    }
}

public sealed class WriteSettingsStep : IInstallStep
{
    public string Title => "Gravar as configurações";

    public Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var service in ctx.TargetServices)
        {
            var dir = ctx.ServiceDir(service);
            var defaultsPath = Path.Combine(dir, "appsettings.json");
            var clientPath = Path.Combine(dir, $"appsettings.{ctx.Environment}.json");

            var defaults = File.Exists(defaultsPath) ? JsonFile.Load(defaultsPath) : new();
            var client = File.Exists(clientPath) ? JsonFile.Load(clientPath) : null;
            var result = AppSettingsMerger.Merge(
                defaults,
                client,
                ServiceSettingsMapper.ToEdits(service.Kind, ctx.Settings),
                ServiceSettingsMapper.DropPaths(service.Kind));

            JsonFile.Save(clientPath, result.Result);
            ctx.Log.Report($"{service.Kind}: {clientPath} gravado ({result.ChangedKeys.Count} chave(s) alterada(s)).");
            foreach (var key in result.ChangedKeys)
            {
                ctx.Log.Report($"  alterada: {key}");
            }
            foreach (var key in result.NewKeys)
            {
                ctx.Log.Report($"  chave nova (usa o valor padrão): {key}");
            }
            foreach (var key in result.ObsoleteKeys)
            {
                ctx.Log.Report($"  possivelmente obsoleta (mantida): {key}");
            }

            if (service.Kind == ServiceKind.Reports && !string.IsNullOrWhiteSpace(ctx.Settings.LogoSourcePath))
            {
                var logo = Path.Combine(dir, ClientFiles.LogoPath);
                Directory.CreateDirectory(Path.GetDirectoryName(logo)!);
                File.Copy(ctx.Settings.LogoSourcePath, logo, overwrite: true);
                ctx.Log.Report("Logo da empresa copiado.");
            }
        }
        return Task.CompletedTask;
    }

    // Os arquivos voltam pelo rollback da cópia (o backup inclui o appsettings do cliente).
    public Task RollbackAsync(InstallContext ctx, CancellationToken ct) => Task.CompletedTask;
}

public sealed class WriteManifestStep(Func<DateTime> now) : IInstallStep
{
    public string Title => "Registrar a instalação";

    public Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        new InstallManifest(ctx.Version, ctx.Instance, ctx.BasePath, ctx.Environment, ctx.TargetServices, now()).Save(ctx.BasePath);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(InstallContext ctx, CancellationToken ct)
    {
        if (ctx.Detection.Manifest is { } previous)
        {
            previous.Save(ctx.BasePath);
        }
        else
        {
            File.Delete(Path.Combine(ctx.BasePath, InstallManifest.FileName));
        }
        return Task.CompletedTask;
    }
}
