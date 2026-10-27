using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Settings;
using SiagroB1.Installer.Wizard;

namespace SiagroB1.Installer.Tests.Wizard;

/// <summary>
/// Carrega a janela de verdade e desenha cada página: pega erro de XAML/template que só aparece em
/// runtime. Com SIAGRO_INSTALLER_SNAPSHOTS=&lt;pasta&gt;, salva um PNG por página para conferência visual.
/// </summary>
public class WindowRenderTests
{
    private sealed class Shell(PageViewModel current) : ObservableObject
    {
        public PageViewModel Current { get; } = current;
        public string Errors => "Exemplo de mensagem de validação.";
        public string NextText => "Avançar";
        public int Step => 3;
        public RelayCommand NextCommand { get; } = new(() => { });
        public RelayCommand BackCommand { get; } = new(() => { });
        public RelayCommand CancelCommand { get; } = new(() => { });
    }

    private sealed class NoQuery : IServiceQuery
    {
        public IEnumerable<(string Name, string BinPath)> All() => [];
    }

    [Fact]
    public void AllPagesRender()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Render();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static void Render()
    {
        var app = Application.Current as App ?? new App();
        app.InitializeComponent();
        // Sem Application.Run aqui: não bombear o dispatcher (PushFrame trava fora do Run);
        // UpdateLayout + RenderTargetBitmap bastam para aplicar bindings e desenhar.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var state = new WizardState(new PayloadInfo(Path.GetTempPath(), "1.1.300"));
        state.Settings.SqlServer = "192.168.1.144,1433";
        state.Settings.CommonDatabase = "IDX_SIAGRO_COMMON";
        state.Settings.ErpMode = "SAPB1";
        state.MigrationReport = new MigrationReport([new PendingMigrations("Common", ["M1"])]);
        var services = new WizardServices((_, _) => Task.FromResult<string?>(null), _ => false, new NoQuery(), null!, null!);
        var progress = new ProgressPageViewModel(state, services);

        var pages = new PageViewModel[]
        {
            new WelcomePageViewModel(state, services),
            new FolderPageViewModel(state, services),
            new DatabasePageViewModel(state, services),
            new ErpPageViewModel(state, services),
            new CompanyPortsPageViewModel(state, services),
            new IntegrationsPageViewModel(state, services),
            new NewKeysPageViewModel(state, services),
            new SummaryPageViewModel(state, services),
            progress
        };

        var outDir = Environment.GetEnvironmentVariable("SIAGRO_INSTALLER_SNAPSHOTS");
        var index = 0;
        foreach (var page in pages)
        {
            if (page is not WelcomePageViewModel)
            {
                page.OnEnter();
            }
            var window = new MainWindow { DataContext = new Shell(page), ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
            window.Show();
            window.UpdateLayout();

            if (!string.IsNullOrEmpty(outDir))
            {
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(outDir);
                using var file = File.Create(Path.Combine(outDir, $"{index++:00}-{page.GetType().Name}.png"));
                encoder.Save(file);
            }
            window.Close();
        }
    }
}
