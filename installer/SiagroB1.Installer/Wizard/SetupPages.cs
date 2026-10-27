using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Wizard;

public sealed class WelcomePageViewModel : PageViewModel
{
    private string _status = "";
    private bool _confirmRemoval;
    private string _removalResult = "";

    public WelcomePageViewModel(WizardState state, WizardServices services) : base(state, services)
    {
        RemoveServicesCommand = new AsyncRelayCommand(RemoveServicesAsync, () => ConfirmRemoval && State.IsUpdate);
    }

    public override string Title => "Bem-vindo ao instalador do SiagroB1";
    public string Version => State.Payload.Version;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool HasInstallation => State.IsUpdate;
    public bool ConfirmRemoval { get => _confirmRemoval; set => Set(ref _confirmRemoval, value); }
    public string RemovalResult { get => _removalResult; private set => Set(ref _removalResult, value); }
    public AsyncRelayCommand RemoveServicesCommand { get; }

    public override void OnEnter()
    {
        State.Detection = new InstallationDetector(Services.ServiceQuery).Detect(State.BasePath);
        var from = State.Detection.Manifest?.Version;
        Status = State.IsUpdate
            ? $"Foi encontrada uma instalação em {State.BasePath}{(from is null ? "" : $" (versão {from})")}. Avance para atualizá-la para a versão {Version}, ou escolha outra pasta para uma nova instância."
            : $"Nenhuma instalação encontrada em {State.BasePath}. Avance para instalar a versão {Version}.";
        OnPropertyChanged(nameof(HasInstallation));
    }

    public override IReadOnlyList<string> Validate() => [];

    private async Task RemoveServicesAsync()
    {
        var manager = Services.ServiceManager();
        var removed = new List<string>();
        foreach (var service in State.Detection.Services)
        {
            await manager.StopAsync(service.ServiceName, TimeSpan.FromSeconds(60), CancellationToken.None);
            await manager.DeleteAsync(service.ServiceName, CancellationToken.None);
            removed.Add(service.ServiceName);
        }
        RemovalResult = $"Serviços removidos: {string.Join(", ", removed)}. Pastas e bancos de dados foram mantidos.";
        ConfirmRemoval = false;
        OnEnter();
    }
}

public sealed class FolderPageViewModel(WizardState state, WizardServices services) : PageViewModel(state, services)
{
    private string? _loadError;

    public override string Title => "Pasta e instância";
    public override string Subtitle => "Onde o SiagroB1 fica instalado. Use outra instância para ter, por exemplo, Produção e Homologação na mesma máquina.";

    public string BasePath { get => State.BasePath; set { State.BasePath = value; _loadError = null; OnPropertyChanged(); } }
    public string Instance { get => State.Instance; set { State.Instance = value; OnPropertyChanged(); } }
    public string Environment { get => State.Environment; set { State.Environment = value; OnPropertyChanged(); } }

    public override IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(BasePath) || !Path.IsPathFullyQualified(BasePath))
        {
            errors.Add("Informe a pasta completa, por exemplo C:\\IdxSistemas\\SiagroB1\\Producao.");
        }
        if (string.IsNullOrWhiteSpace(Instance) || !Instance.All(char.IsLetterOrDigit))
        {
            errors.Add("O nome da instância deve ter só letras e números.");
        }
        if (string.IsNullOrWhiteSpace(Environment) || !Environment.All(c => char.IsLetterOrDigit(c) || c == '-'))
        {
            errors.Add("Informe o ambiente (por exemplo Production).");
        }
        if (_loadError is not null)
        {
            errors.Add(_loadError);
        }
        return errors;
    }

    /// <summary>Detecta a instalação na pasta escolhida e pré-preenche as próximas telas com os arquivos do cliente.</summary>
    public override void OnLeave()
    {
        _loadError = null;
        State.Detection = new InstallationDetector(Services.ServiceQuery).Detect(State.BasePath);
        if (State.Detection.Manifest is { } manifest)
        {
            State.Instance = manifest.Instance;
        }
        if (State.Detection.Services.FirstOrDefault() is { } any)
        {
            State.Environment = any.Environment;
        }

        try
        {
            foreach (var kind in Enum.GetValues<ServiceKind>())
            {
                var installed = State.Detection.Services.FirstOrDefault(s => s.Kind == kind);
                var dir = installed is not null ? Path.GetDirectoryName(installed.ExePath)! : Path.Combine(State.BasePath, kind.ToString());
                var file = Path.Combine(dir, $"appsettings.{State.Environment}.json");
                State.ClientFiles[kind] = File.Exists(file) ? JsonFile.Load(file) : null;
            }
        }
        catch (InvalidSettingsFileException ex)
        {
            _loadError = ex.Message + " Corrija o arquivo antes de continuar.";
            return;
        }

        var settings = ServiceSettingsMapper.FromClientFiles(
            State.ClientFiles[ServiceKind.Web], State.ClientFiles[ServiceKind.Gateway], State.ClientFiles[ServiceKind.Reports]);
        foreach (var service in State.Detection.Services)
        {
            switch (service.Kind)
            {
                case ServiceKind.Gateway: settings.GatewayPort = service.Port; break;
                case ServiceKind.Web: settings.WebPort = service.Port; break;
                case ServiceKind.Reports: settings.ReportsPort = service.Port; break;
            }
        }
        if (string.IsNullOrEmpty(settings.ReportsInternalApiKey))
        {
            settings.ReportsInternalApiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }
        State.Settings = settings;
    }
}

public sealed class DatabasePageViewModel : PageViewModel
{
    private bool _connectionTested;
    private string _testResult = "";

    public DatabasePageViewModel(WizardState state, WizardServices services) : base(state, services)
    {
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
    }

    public override string Title => "Banco de dados";
    public override string Subtitle => "SQL Server onde ficam os bancos do SiagroB1. Outras empresas são lidas da tabela de empresas do banco Common.";

    private InstallSettings S => State.Settings;
    private void Changed() => ConnectionTested = false;

    public string SqlServer { get => S.SqlServer; set { S.SqlServer = value; Changed(); OnPropertyChanged(); } }
    public bool UseWindowsAuth { get => S.UseWindowsAuth; set { S.UseWindowsAuth = value; Changed(); OnPropertyChanged(); } }
    public string SqlUser { get => S.SqlUser; set { S.SqlUser = value; Changed(); OnPropertyChanged(); } }
    public string SqlPassword { get => S.SqlPassword; set { S.SqlPassword = value; Changed(); OnPropertyChanged(); } }
    public string CommonDatabase { get => S.CommonDatabase; set { S.CommonDatabase = value; Changed(); OnPropertyChanged(); } }
    public string CompanyDatabase { get => S.CompanyDatabase; set { S.CompanyDatabase = value; Changed(); OnPropertyChanged(); } }

    public bool ConnectionTested { get => _connectionTested; private set => Set(ref _connectionTested, value); }
    public string TestResult { get => _testResult; private set => Set(ref _testResult, value); }
    public AsyncRelayCommand TestConnectionCommand { get; }

    public async Task TestConnectionAsync()
    {
        TestResult = "Testando...";
        var errors = new List<string>();
        foreach (var name in new[] { "SiagroCommon", "SiagroDB" })
        {
            var cs = ServiceSettingsMapper.ToEdits(ServiceKind.Web, S)[$"ConnectionStrings:{name}"]!.GetValue<string>();
            var builder = new SqlConnectionStringBuilder(cs);
            if (await Services.TestConnection(cs, CancellationToken.None) is { } error)
            {
                errors.Add($"{builder.InitialCatalog}: {error}");
            }
        }
        ConnectionTested = errors.Count == 0;
        TestResult = ConnectionTested ? "Conexão bem-sucedida." : string.Join(System.Environment.NewLine, errors);
    }

    public override IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SqlServer)) errors.Add("Informe o servidor SQL.");
        if (!UseWindowsAuth && string.IsNullOrWhiteSpace(SqlUser)) errors.Add("Informe o usuário do SQL Server.");
        if (string.IsNullOrWhiteSpace(CommonDatabase)) errors.Add("Informe o banco Common.");
        if (string.IsNullOrWhiteSpace(CompanyDatabase)) errors.Add("Informe o banco da empresa.");
        if (errors.Count == 0 && !ConnectionTested) errors.Add("Clique em \"Testar conexão\" antes de avançar.");
        return errors;
    }
}

public sealed class ErpPageViewModel(WizardState state, WizardServices services) : PageViewModel(state, services)
{
    private InstallSettings S => State.Settings;

    public override string Title => "Modo de operação";
    public override string Subtitle => "STANDALONE: o SiagroB1 trabalha sozinho. SAPB1: integra com o SAP Business One.";

    public bool IsSapB1
    {
        get => S.IsSapB1;
        set { S.ErpMode = value ? "SAPB1" : "STANDALONE"; OnPropertyChanged(); OnPropertyChanged(nameof(IsStandalone)); }
    }
    public bool IsStandalone { get => !IsSapB1; set => IsSapB1 = !value; }
    public string SapDatabase { get => S.SapDatabase; set { S.SapDatabase = value; OnPropertyChanged(); } }
    public string SapCommonDatabase { get => S.SapCommonDatabase; set { S.SapCommonDatabase = value; OnPropertyChanged(); } }
    public string SapServiceLayerUrl { get => S.SapServiceLayerUrl; set { S.SapServiceLayerUrl = value; OnPropertyChanged(); } }

    public override IReadOnlyList<string> Validate()
    {
        if (!IsSapB1) return [];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SapDatabase)) errors.Add("Informe o banco da empresa no SAP.");
        if (string.IsNullOrWhiteSpace(SapCommonDatabase)) errors.Add("Informe o banco SBO-COMMON.");
        if (!Uri.TryCreate(SapServiceLayerUrl, UriKind.Absolute, out _)) errors.Add("Informe a URL do Service Layer.");
        return errors;
    }

    public override void OnEnter()
    {
        if (IsSapB1 && string.IsNullOrWhiteSpace(SapCommonDatabase))
        {
            SapCommonDatabase = "SBO-COMMON";
        }
    }
}

public sealed class CompanyPortsPageViewModel(WizardState state, WizardServices services) : PageViewModel(state, services)
{
    private InstallSettings S => State.Settings;

    public override string Title => "Empresa e portas";
    public override string Subtitle => "Só o Gateway precisa ser acessível pela rede; Web e Relatórios ficam internos.";

    public string CompanyName { get => S.CompanyName; set { S.CompanyName = value; OnPropertyChanged(); } }
    public int GatewayPort { get => S.GatewayPort; set { S.GatewayPort = value; OnPropertyChanged(); } }
    public int WebPort { get => S.WebPort; set { S.WebPort = value; OnPropertyChanged(); } }
    public int ReportsPort { get => S.ReportsPort; set { S.ReportsPort = value; OnPropertyChanged(); } }
    public string PublicBaseUrl { get => S.PublicBaseUrl; set { S.PublicBaseUrl = value; OnPropertyChanged(); } }
    public string? LogoSourcePath { get => S.LogoSourcePath; set { S.LogoSourcePath = value; OnPropertyChanged(); } }

    public override void OnEnter()
    {
        if (string.IsNullOrWhiteSpace(PublicBaseUrl))
        {
            PublicBaseUrl = $"http://{System.Environment.MachineName.ToLowerInvariant()}:{GatewayPort}";
        }
    }

    public override IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CompanyName)) errors.Add("Informe o nome da empresa.");
        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            errors.Add("Informe a URL de acesso completa, por exemplo http://servidor:55000.");
        }
        if (!string.IsNullOrWhiteSpace(LogoSourcePath) && !File.Exists(LogoSourcePath))
        {
            errors.Add("O arquivo de logo informado não existe.");
        }

        var ports = new Dictionary<ServiceKind, int>
        {
            [ServiceKind.Gateway] = GatewayPort,
            [ServiceKind.Web] = WebPort,
            [ServiceKind.Reports] = ReportsPort
        };
        var own = State.Detection.Services.Select(s => s.Port).ToHashSet();
        errors.AddRange(PortChecker.Validate(ports, Services.IsPortInUse, own));
        return errors;
    }
}
