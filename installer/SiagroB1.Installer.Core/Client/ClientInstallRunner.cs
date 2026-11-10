using System.IO.Compression;
using SiagroB1.Installer.Core.Services;

namespace SiagroB1.Installer.Core.Client;

/// <summary>Instala, atualiza e remove o serviço do SiagroB1.Client. Sempre idempotente: rodar de novo atualiza.</summary>
public sealed class ClientInstallRunner(IServiceManager services, Action<string> log)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task InstallAsync(ClientInstallSettings settings, Stream payloadZip, CancellationToken ct)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        if (services.Exists(ClientInstall.ServiceName))
        {
            log("Parando o serviço atual...");
            await services.StopAsync(ClientInstall.ServiceName, Timeout, ct);
        }

        log($"Copiando arquivos para {settings.InstallDirectory}...");
        Directory.CreateDirectory(settings.InstallDirectory);
        ZipFile.ExtractToDirectory(payloadZip, settings.InstallDirectory, overwriteFiles: true);

        log("Gravando a configuração...");
        settings.Write();

        var exe = Path.Combine(settings.InstallDirectory, ClientInstall.ExeName);
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"O pacote não contém {ClientInstall.ExeName}.", exe);
        }

        log("Registrando o serviço do Windows...");
        await services.CreateOrReplaceAsync(
            ClientInstall.ServiceName, $"\"{exe}\"", ClientInstall.DisplayName, ClientInstall.Description, ct);

        log("Iniciando o serviço...");
        await services.StartAsync(ClientInstall.ServiceName, Timeout, ct);
        log("Pronto: o leitor da balança está rodando.");
    }

    /// <summary>Remove o serviço e mantém a pasta, para não perder a configuração caso seja reinstalado.</summary>
    public async Task UninstallAsync(CancellationToken ct)
    {
        if (!services.Exists(ClientInstall.ServiceName))
        {
            log("O serviço não está instalado.");
            return;
        }
        await services.StopAsync(ClientInstall.ServiceName, Timeout, ct);
        await services.DeleteAsync(ClientInstall.ServiceName, ct);
        log("Serviço removido. Os arquivos foram mantidos na pasta de instalação.");
    }
}
