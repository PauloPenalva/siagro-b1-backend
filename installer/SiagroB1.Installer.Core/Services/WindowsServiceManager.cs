using System.Runtime.Versioning;
using System.ServiceProcess;

namespace SiagroB1.Installer.Core.Services;

public sealed class ServiceTimeoutException(string name, string action)
    : Exception($"O serviço \"{name}\" não conseguiu {action} dentro do tempo limite.")
{
    public string ServiceName { get; } = name;
}

public sealed class ServiceCommandException(string message) : Exception(message);

public interface IServiceManager
{
    bool Exists(string name);
    bool IsRunning(string name);
    Task StopAsync(string name, TimeSpan timeout, CancellationToken ct);
    Task StartAsync(string name, TimeSpan timeout, CancellationToken ct);
    Task CreateOrReplaceAsync(string name, string binPath, string displayName, string description, CancellationToken ct);
    Task DeleteAsync(string name, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsServiceManager(IProcessRunner runner) : IServiceManager
{
    private const string Sc = "sc.exe";

    public bool Exists(string name) =>
        ServiceController.GetServices().Any(s => string.Equals(s.ServiceName, name, StringComparison.OrdinalIgnoreCase));

    public bool IsRunning(string name)
    {
        if (!Exists(name))
        {
            return false;
        }
        using var sc = new ServiceController(name);
        return sc.Status == ServiceControllerStatus.Running;
    }

    public Task StopAsync(string name, TimeSpan timeout, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (!Exists(name))
            {
                return;
            }
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Stopped)
            {
                return;
            }
            if (sc.Status is not ServiceControllerStatus.StopPending)
            {
                sc.Stop();
            }
            try
            {
                sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                throw new ServiceTimeoutException(name, "parar");
            }
        }, ct);

    public Task StartAsync(string name, TimeSpan timeout, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Running)
            {
                return;
            }
            sc.Start();
            try
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                throw new ServiceTimeoutException(name, "iniciar");
            }
        }, ct);

    public async Task CreateOrReplaceAsync(string name, string binPath, string displayName, string description, CancellationToken ct)
    {
        // "config" em vez de apagar e recriar: preserva conta de logon e permissões que o cliente
        // possa ter ajustado no serviço.
        var args = Exists(name)
            ? ServiceCommandBuilder.ConfigArgs(name, binPath, displayName)
            : ServiceCommandBuilder.CreateArgs(name, binPath, displayName);
        await Run(args, ct);
        await Run(ServiceCommandBuilder.DescriptionArgs(name, description), ct);
        await Run(ServiceCommandBuilder.FailureArgs(name), ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        if (Exists(name))
        {
            await Run(ServiceCommandBuilder.DeleteArgs(name), ct);
        }
    }

    private async Task Run(IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await runner.RunAsync(Sc, args, null, null, ct);
        if (result.ExitCode != 0)
        {
            throw new ServiceCommandException($"sc.exe {args[0]} {args[1]} falhou (código {result.ExitCode}): {result.Output.Trim()}");
        }
    }
}
