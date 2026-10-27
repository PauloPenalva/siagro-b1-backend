using System.Windows;
using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Payload;
using SiagroB1.Installer.Wizard;

namespace SiagroB1.Installer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        PayloadInfo? payload;
        try
        {
            payload = PayloadExtractor.Locate(e.Args);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Não foi possível abrir o pacote de instalação: {ex.Message}", "Instalador SiagroB1", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        if (payload is null)
        {
            MessageBox.Show("Este instalador não contém o pacote da versão (payload.zip).", "Instalador SiagroB1", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var runner = new ProcessRunner();
        var services = new WizardServices(
            SqlConnectionTester.TestAsync,
            PortChecker.IsInUse,
            new RegistryServiceQuery(),
            () => new WindowsServiceManager(runner),
            () => new MigrationRunner(runner, new SqlCompanyLister(), new SqlMigrationHistory()));

        var window = new MainWindow();
        window.DataContext = new WizardViewModel(new WizardState(payload), services, window.Close);
        MainWindow = window;
        window.Show();
    }
}
