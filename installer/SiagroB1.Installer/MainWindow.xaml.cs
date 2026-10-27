using System.ComponentModel;
using System.Windows;
using SiagroB1.Installer.Wizard;

namespace SiagroB1.Installer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Fechar no meio da instalação deixaria serviços parados e arquivos pela metade.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is WizardViewModel { Current: ProgressPageViewModel { IsRunning: true } })
        {
            MessageBox.Show(this, "Aguarde o fim da instalação.", "Instalador SiagroB1", MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
