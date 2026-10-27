using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Core.Execution.Steps;

internal static class ServiceOrder
{
    // Para: quem recebe tráfego primeiro (Gateway) sai primeiro. Inicia: na ordem inversa.
    public static IEnumerable<InstalledService> StopOrder(IEnumerable<InstalledService> services) =>
        services.OrderBy(s => s.Kind switch { ServiceKind.Gateway => 0, ServiceKind.Reports => 1, _ => 2 });

    public static IEnumerable<InstalledService> StartOrder(IEnumerable<InstalledService> services) =>
        StopOrder(services).Reverse();

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
}

public sealed class StopServicesStep(IServiceManager services) : IInstallStep
{
    private readonly List<string> _stopped = [];

    public string Title => "Parar os serviços";

    public async Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var service in ServiceOrder.StopOrder(ctx.TargetServices))
        {
            if (!services.Exists(service.ServiceName) || !services.IsRunning(service.ServiceName))
            {
                continue;
            }
            ctx.Log.Report($"Parando {service.ServiceName}...");
            await services.StopAsync(service.ServiceName, ServiceOrder.Timeout, ct);
            _stopped.Add(service.ServiceName);
        }
    }

    public async Task RollbackAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var name in Enumerable.Reverse(_stopped))
        {
            ctx.Log.Report($"Religando {name}...");
            await services.StartAsync(name, ServiceOrder.Timeout, ct);
        }
    }
}

public sealed class RegisterServicesStep(IServiceManager services) : IInstallStep
{
    private readonly List<string> _created = [];
    private readonly List<InstalledService> _reconfigured = [];

    public string Title => "Registrar os serviços do Windows";

    public async Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var service in ctx.TargetServices)
        {
            var existed = services.Exists(service.ServiceName);
            var binPath = ServiceCommandBuilder.BinPath(service.ExePath, service.Environment, service.Port);
            ctx.Log.Report($"{(existed ? "Atualizando" : "Criando")} serviço {service.ServiceName}: {binPath}");
            await services.CreateOrReplaceAsync(service.ServiceName, binPath, DisplayName(service, ctx.Instance), Description(service.Kind), ct);
            if (existed)
            {
                _reconfigured.Add(service);
            }
            else
            {
                _created.Add(service.ServiceName);
            }
        }
    }

    public async Task RollbackAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var name in _created)
        {
            await services.DeleteAsync(name, ct);
        }
        foreach (var service in _reconfigured)
        {
            var before = ctx.Detection.Services.FirstOrDefault(s => s.ServiceName.Equals(service.ServiceName, StringComparison.OrdinalIgnoreCase));
            if (before is not null)
            {
                var binPath = ServiceCommandBuilder.BinPath(before.ExePath, before.Environment, before.Port);
                await services.CreateOrReplaceAsync(before.ServiceName, binPath, DisplayName(before, ctx.Instance), Description(before.Kind), ct);
            }
        }
    }

    public static string DisplayName(InstalledService service, string instance) =>
        $"IDX Sistemas | SiagroB1 {Label(service.Kind)} - {instance} ({service.Port})";

    private static string Description(ServiceKind kind) => $"SiagroB1 {Label(kind)}";

    private static string Label(ServiceKind kind) => kind switch
    {
        ServiceKind.Web => "API",
        ServiceKind.Gateway => "Gateway",
        _ => "Relatórios"
    };
}

public sealed class StartServicesStep(IServiceManager services) : IInstallStep
{
    private readonly List<string> _started = [];

    public string Title => "Iniciar os serviços";

    public async Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var service in ServiceOrder.StartOrder(ctx.TargetServices))
        {
            ctx.Log.Report($"Iniciando {service.ServiceName}...");
            await services.StartAsync(service.ServiceName, ServiceOrder.Timeout, ct);
            _started.Add(service.ServiceName);
        }
    }

    public async Task RollbackAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var name in Enumerable.Reverse(_started))
        {
            await services.StopAsync(name, ServiceOrder.Timeout, ct);
        }
    }
}

public sealed class ProbeStep(TimeSpan timeout) : IInstallStep
{
    public string Title => "Testar se os serviços respondem";

    public async Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
    {
        foreach (var service in ctx.TargetServices)
        {
            var (ok, status) = await EndpointProbe.ProbeAsync(new Uri($"http://localhost:{service.Port}/"), timeout);
            if (ok)
            {
                ctx.Log.Report($"✔ {service.ServiceName} respondendo (HTTP {status}).");
            }
            else
            {
                // Não desfaz a instalação: o serviço pode só estar demorando a subir.
                var warning = $"{service.ServiceName} não respondeu em http://localhost:{service.Port}/" +
                              (status is null ? "." : $" (HTTP {status}).") + " Verifique o log do serviço.";
                ctx.Warnings.Add(warning);
                ctx.Log.Report($"⚠ {warning}");
            }
        }
    }

    public Task RollbackAsync(InstallContext ctx, CancellationToken ct) => Task.CompletedTask;
}
