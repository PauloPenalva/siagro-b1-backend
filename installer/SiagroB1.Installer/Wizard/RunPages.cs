using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using SiagroB1.Installer.Core.Execution;
using SiagroB1.Installer.Core.Execution.Steps;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Wizard;

public sealed class SummaryPageViewModel(WizardState state, WizardServices services) : PageViewModel(state, services)
{
    private string _migrationStatus = "";

    public override string Title => "Resumo";
    public override string Subtitle => "Confira antes de instalar. Nada foi alterado até aqui.";

    public ObservableCollection<string> Lines { get; } = [];
    public string MigrationStatus { get => _migrationStatus; private set => Set(ref _migrationStatus, value); }
    public bool HasPendingMigrations => State.MigrationReport?.HasPending == true;

    public bool DatabaseBackupConfirmed
    {
        get => State.DatabaseBackupConfirmed;
        set { State.DatabaseBackupConfirmed = value; OnPropertyChanged(); }
    }

    public string? DdlUser { get => State.DdlUser; set { State.DdlUser = value; OnPropertyChanged(); } }
    public string? DdlPassword { get => State.DdlPassword; set { State.DdlPassword = value; OnPropertyChanged(); } }

    public override void OnEnter() => BuildLines();

    public override async Task OnEnterAsync()
    {
        MigrationStatus = "Verificando atualizações de banco pendentes...";
        try
        {
            var ctx = new InstallContext(State.Settings, State.Detection, State.BasePath, State.Instance, State.Environment,
                State.Payload.Dir, State.Payload.Version, new Progress<string>())
            { DdlUser = State.DdlUser, DdlPassword = State.DdlPassword };
            State.MigrationReport = await Services.Migrations().ListPendingAsync(MigrateStep.Request(ctx), CancellationToken.None);
            var pending = State.MigrationReport.Pending.Where(p => p.Migrations.Count > 0).ToList();
            MigrationStatus = pending.Count == 0
                ? "Os bancos já estão atualizados."
                : "Atualizações de banco pendentes: " + string.Join("; ", pending.Select(p => $"{p.Label}: {p.Migrations.Count}"));
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or IOException)
        {
            MigrationStatus = $"Não foi possível verificar os bancos: {SecretMasker.MaskText(ex.Message)}";
        }
        OnPropertyChanged(nameof(HasPendingMigrations));
    }

    public void BuildLines()
    {
        var s = State.Settings;
        Lines.Clear();
        var from = State.Detection.Manifest?.Version;
        Lines.Add(State.IsUpdate
            ? $"Atualização{(from is null ? "" : $" da versão {from}")} para a versão {State.Payload.Version}"
            : $"Nova instalação da versão {State.Payload.Version}");
        Lines.Add($"Pasta: {State.BasePath}  ·  Instância: {State.Instance}  ·  Ambiente: {State.Environment}");

        var ctx = new InstallContext(s, State.Detection, State.BasePath, State.Instance, State.Environment, State.Payload.Dir, State.Payload.Version, new Progress<string>());
        foreach (var service in InstallPlanBuilder.ResolveServices(ctx))
        {
            Lines.Add($"Serviço {service.Kind}: {service.ServiceName}, porta {service.Port}");
        }

        Lines.Add($"SQL Server: {s.SqlServer}  ·  " + (s.UseWindowsAuth ? "autenticação do Windows" : $"usuário {s.SqlUser}"));
        Lines.Add($"Banco Common: {s.CommonDatabase}  ·  Banco da empresa: {s.CompanyDatabase}");
        Lines.Add(s.IsSapB1
            ? $"Modo SAPB1: banco {s.SapDatabase}, {s.SapCommonDatabase}, Service Layer {s.SapServiceLayerUrl}"
            : "Modo STANDALONE");
        Lines.Add($"Empresa: {s.CompanyName}  ·  Acesso: {s.PublicBaseUrl}");

        var integrations = s.Integrations.Keys.Concat(s.ServiceEdits.Values.SelectMany(e => e.Keys)).ToList();
        if (integrations.Count > 0)
        {
            Lines.Add("Configurações alteradas: " + string.Join(", ", integrations));
        }
        if (State.IsUpdate)
        {
            Lines.Add("Antes de copiar, os serviços serão parados e a versão atual vai para a pasta backups.");
        }

        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i] = SecretMasker.MaskText(Lines[i]);
        }
    }

    public override IReadOnlyList<string> Validate() =>
        HasPendingMigrations && !DatabaseBackupConfirmed ? ["Confirme que foi feito o backup do banco de dados."] : [];
}

public sealed class ProgressPageViewModel(WizardState state, WizardServices services) : PageViewModel(state, services)
{
    private bool _isRunning;

    public override string Title => "Instalando";
    public ObservableCollection<string> Log { get; } = [];
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }
    public InstallOutcome? Outcome { get; private set; }
    public InstallContext? Context { get; private set; }
    public string? LogPath { get; private set; }

    public override IReadOnlyList<string> Validate() => IsRunning ? ["Aguarde o fim da instalação."] : [];

    public override async Task OnEnterAsync()
    {
        if (Outcome is not null) return;
        IsRunning = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        using var log = new SetupLog(State.BasePath, DateTime.Now, line =>
        {
            if (dispatcher is null) Log.Add(line);
            else dispatcher.Invoke(() => Log.Add(line));
        });
        LogPath = log.FilePath;

        var ctx = new InstallContext(State.Settings, State.Detection, State.BasePath, State.Instance, State.Environment,
            State.Payload.Dir, State.Payload.Version, log)
        {
            MigrationReport = State.MigrationReport,
            DatabaseBackupConfirmed = State.DatabaseBackupConfirmed,
            DdlUser = State.DdlUser,
            DdlPassword = State.DdlPassword
        };
        Context = ctx;
        var steps = InstallPlanBuilder.Build(ctx, new InstallDependencies(Services.ServiceManager(), Services.Migrations()));
        Outcome = await Task.Run(() => InstallExecutor.RunAsync(steps, ctx, CancellationToken.None));
        log.Report(Outcome.Success ? "Instalação concluída." : $"Instalação NÃO concluída: {Outcome.Error}");
        IsRunning = false;
    }
}

public sealed class DonePageViewModel : PageViewModel
{
    private readonly ProgressPageViewModel _progress;

    public DonePageViewModel(WizardState state, WizardServices services, ProgressPageViewModel progress) : base(state, services)
    {
        _progress = progress;
        OpenBrowserCommand = new RelayCommand(() => Open(State.Settings.PublicBaseUrl), () => Success);
        OpenLogCommand = new RelayCommand(() => Open(_progress.LogPath!), () => _progress.LogPath is not null);
    }

    public override string Title => Success ? "Instalação concluída" : "A instalação não foi concluída";
    public bool Success => _progress.Outcome?.Success == true;
    public ObservableCollection<string> Lines { get; } = [];
    public RelayCommand OpenBrowserCommand { get; }
    public RelayCommand OpenLogCommand { get; }

    public override void OnEnter()
    {
        Lines.Clear();
        var outcome = _progress.Outcome;
        if (outcome is null) return;
        if (!outcome.Success)
        {
            Lines.Add($"Falhou em \"{outcome.FailedStep}\": {outcome.Error}");
            Lines.Add("As alterações nos arquivos e serviços foram desfeitas e a versão anterior foi religada.");
        }
        var manager = Services.ServiceManager();
        foreach (var service in _progress.Context?.TargetServices ?? [])
        {
            var running = manager.Exists(service.ServiceName) && manager.IsRunning(service.ServiceName);
            Lines.Add($"{service.ServiceName}: {(running ? "em execução" : "parado")}");
        }
        foreach (var warning in _progress.Context?.Warnings ?? [])
        {
            Lines.Add($"Atenção: {warning}");
        }
        if (_progress.LogPath is not null)
        {
            Lines.Add($"Log: {_progress.LogPath}");
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Success));
    }

    public override IReadOnlyList<string> Validate() => [];

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
