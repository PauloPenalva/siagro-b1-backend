using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Execution.Steps;
using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Execution;

public sealed record InstallDependencies(IServiceManager Services, MigrationRunner Migrations);

public static class InstallPlanBuilder
{
    public static IReadOnlyList<IInstallStep> Build(InstallContext ctx, InstallDependencies deps)
    {
        ctx.TargetServices = ResolveServices(ctx);

        var steps = new List<IInstallStep>();
        if (ctx.IsUpdate)
        {
            steps.Add(new StopServicesStep(deps.Services));
            steps.Add(new BackupStep(() => DateTime.Now));
        }
        steps.Add(new CopyFilesStep());
        steps.Add(new WriteSettingsStep());
        steps.Add(new MigrateStep(deps.Migrations));
        steps.Add(new RegisterServicesStep(deps.Services));
        steps.Add(new StartServicesStep(deps.Services));
        steps.Add(new ProbeStep(TimeSpan.FromSeconds(30)));
        steps.Add(new WriteManifestStep(() => DateTime.Now));
        return steps;
    }

    /// <summary>
    /// Serviço já instalado mantém nome e pasta (inclusive os nomes dos scripts antigos); o que falta
    /// ganha o padrão siagrob1-&lt;instância&gt;-&lt;tipo&gt; em &lt;base&gt;\&lt;Tipo&gt;. Porta e ambiente vêm do assistente.
    /// </summary>
    public static IReadOnlyList<InstalledService> ResolveServices(InstallContext ctx)
    {
        var result = new List<InstalledService>();
        foreach (var kind in new[] { ServiceKind.Web, ServiceKind.Gateway, ServiceKind.Reports })
        {
            var port = kind switch
            {
                ServiceKind.Web => ctx.Settings.WebPort,
                ServiceKind.Gateway => ctx.Settings.GatewayPort,
                _ => ctx.Settings.ReportsPort
            };
            var existing = ctx.Detection.Services.FirstOrDefault(s => s.Kind == kind);
            result.Add(existing is not null
                ? existing with { Environment = ctx.Environment, Port = port }
                : new InstalledService(
                    kind,
                    $"siagrob1-{ctx.Instance.ToLowerInvariant()}-{kind.ToString().ToLowerInvariant()}",
                    Path.Combine(ctx.BasePath, kind.ToString(), $"SiagroB1.{kind}.exe"),
                    ctx.Environment,
                    port));
        }
        return result;
    }
}
