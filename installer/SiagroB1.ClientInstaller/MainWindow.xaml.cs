using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using SiagroB1.Installer.Core.Client;
using SiagroB1.Installer.Core.Services;

namespace SiagroB1.ClientInstaller;

public partial class MainWindow : Window
{
    private const string ResourceName = "client.zip";
    private readonly WindowsServiceManager _services = new(new ProcessRunner());

    public MainWindow()
    {
        InitializeComponent();
        DirBox.Text = ClientInstall.DefaultDirectory;
        LoadExisting();
    }

    private void LoadExisting()
    {
        var installed = _services.Exists(ClientInstall.ServiceName);
        var dir = installed ? InstalledDirectory() ?? ClientInstall.DefaultDirectory : ClientInstall.DefaultDirectory;
        DirBox.Text = dir;

        var current = ClientInstallSettings.FromInstalled(dir);
        if (current is not null)
        {
            CodesBox.Text = current.ScaleCodes;
            UrlBox.Text = current.WebSocketUrl;
            KeyBox.Password = current.ClientKey;
        }

        StatusText.Text = installed
            ? "O leitor já está instalado neste computador. Confirme os dados e clique em Instalar para atualizar."
            : "Preencha os dados abaixo. O serviço é registrado no Windows e inicia sozinho com o computador.";
        UninstallButton.IsEnabled = installed;
    }

    /// <summary>Pasta do exe a partir do ImagePath do serviço no registro.</summary>
    private static string? InstalledDirectory()
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ClientInstall.ServiceName}");
        var image = (key?.GetValue("ImagePath") as string)?.Trim();
        if (string.IsNullOrEmpty(image))
        {
            return null;
        }
        var exe = image.StartsWith('"') ? image.Split('"')[1] : image.Split(' ')[0];
        return Path.GetDirectoryName(exe);
    }

    private ClientInstallSettings Read() => new(CodesBox.Text, UrlBox.Text, KeyBox.Password, DirBox.Text.Trim());

    private void Log(string text) => Dispatcher.Invoke(() =>
    {
        LogBox.AppendText(text + Environment.NewLine);
        LogBox.ScrollToEnd();
    });

    private void SetBusy(bool busy)
    {
        InstallButton.IsEnabled = TestButton.IsEnabled = !busy;
        UninstallButton.IsEnabled = !busy && _services.Exists(ClientInstall.ServiceName);
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        var settings = Read();
        // A pasta de instalação não importa para testar a conexão.
        var errors = settings.Validate().Where(m => !m.Contains("pasta de instalação")).ToList();
        if (errors.Count > 0)
        {
            Log("✖ " + string.Join(Environment.NewLine + "✖ ", errors));
            return;
        }
        SetBusy(true);
        try
        {
            Log("Testando a conexão com o servidor...");
            var result = await ClientConnectionProbe.ProbeAsync(settings.WebSocketUrl, settings.Codes[0], settings.ClientKey);
            Log((result.Ok ? "✔ " : "✖ ") + result.Message);
        }
        finally { SetBusy(false); }
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        var settings = Read();
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            Log("✖ " + string.Join(Environment.NewLine + "✖ ", errors));
            return;
        }

        SetBusy(true);
        try
        {
            await using var payload = OpenPayload();
            await Task.Run(() => new ClientInstallRunner(_services, Log).InstallAsync(settings, payload, default));
            MessageBox.Show(this, "Instalação concluída. O leitor da balança já está rodando.",
                "SiagroB1", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("✖ " + ex.Message);
            MessageBox.Show(this, ex.Message, "Falha na instalação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            LoadExisting();
        }
    }

    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Remover o serviço do leitor da balança? Os arquivos e a configuração serão mantidos.",
                "SiagroB1", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        SetBusy(true);
        try
        {
            await Task.Run(() => new ClientInstallRunner(_services, Log).UninstallAsync(default));
        }
        catch (Exception ex) { Log("✖ " + ex.Message); }
        finally
        {
            SetBusy(false);
            LoadExisting();
        }
    }

    /// <summary>client.zip ao lado do exe (desenvolvimento) ou embutido no próprio exe.</summary>
    private static Stream OpenPayload()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, ResourceName);
        if (File.Exists(beside))
        {
            return File.OpenRead(beside);
        }
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
               ?? throw new InvalidOperationException("Este instalador não contém o pacote do leitor (client.zip).");
    }
}
