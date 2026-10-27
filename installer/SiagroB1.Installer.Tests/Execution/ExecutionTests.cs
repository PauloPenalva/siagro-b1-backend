using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Execution;
using SiagroB1.Installer.Core.Execution.Steps;
using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Execution;

internal sealed class SyncProgress(List<string> lines) : IProgress<string>
{
    public void Report(string value) => lines.Add(value);
}

internal sealed class FakeServiceManager : IServiceManager
{
    public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Calls { get; } = [];
    public Func<string, bool> StopTimesOut { get; set; } = _ => false;

    public bool Exists(string name) => Existing.Contains(name);
    public bool IsRunning(string name) => Running.Contains(name);

    public Task StopAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add($"stop {name}");
        if (StopTimesOut(name))
        {
            throw new ServiceTimeoutException(name, "parar");
        }
        Running.Remove(name);
        return Task.CompletedTask;
    }

    public Task StartAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add($"start {name}");
        Running.Add(name);
        return Task.CompletedTask;
    }

    public Task CreateOrReplaceAsync(string name, string binPath, string displayName, string description, CancellationToken ct)
    {
        Calls.Add($"create {name} {binPath}");
        Existing.Add(name);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        Calls.Add($"delete {name}");
        Existing.Remove(name);
        return Task.CompletedTask;
    }
}

public class InstallExecutorTests
{
    private sealed class RecordingStep(string title, List<string> order, bool fail = false) : IInstallStep
    {
        public string Title => title;

        public Task ExecuteAsync(InstallContext ctx, CancellationToken ct)
        {
            order.Add($"exec {title}");
            return fail ? throw new InvalidOperationException($"falhou {title}") : Task.CompletedTask;
        }

        public Task RollbackAsync(InstallContext ctx, CancellationToken ct)
        {
            order.Add($"rollback {title}");
            return Task.CompletedTask;
        }
    }

    internal static InstallContext Context(string basePath, List<string> log, string? payloadDir = null) =>
        new(new InstallSettings(), new DetectionResult(null, [], false), basePath, "Producao", "Production",
            payloadDir ?? Path.Combine(basePath, "payload"), "1.1.300", new SyncProgress(log));

    [Fact]
    public async Task Run_AllSucceed()
    {
        var order = new List<string>();
        var steps = new IInstallStep[] { new RecordingStep("A", order), new RecordingStep("B", order) };

        var outcome = await InstallExecutor.RunAsync(steps, Context("C:\\x", []), CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(new[] { "exec A", "exec B" }, order);
    }

    [Fact]
    public async Task Run_FailureRollsBackInReverse()
    {
        var order = new List<string>();
        var steps = new IInstallStep[] { new RecordingStep("A", order), new RecordingStep("B", order), new RecordingStep("C", order, fail: true), new RecordingStep("D", order) };

        var outcome = await InstallExecutor.RunAsync(steps, Context("C:\\x", []), CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal("C", outcome.FailedStep);
        Assert.Equal("falhou C", outcome.Error);
        Assert.Equal(new[] { "exec A", "exec B", "exec C", "rollback C", "rollback B", "rollback A" }, order);
    }

    [Fact]
    public async Task Run_StopTimeout_RestartsStoppedServices()
    {
        var services = new FakeServiceManager();
        foreach (var name in new[] { "gw", "rep", "web" })
        {
            services.Existing.Add(name);
            services.Running.Add(name);
        }
        services.StopTimesOut = n => n == "rep";
        var ctx = Context("C:\\x", []);
        ctx.TargetServices =
        [
            new InstalledService(ServiceKind.Gateway, "gw", "C:\\x\\Gateway\\SiagroB1.Gateway.exe", "Production", 55000),
            new InstalledService(ServiceKind.Reports, "rep", "C:\\x\\Reports\\SiagroB1.Reports.exe", "Production", 55200),
            new InstalledService(ServiceKind.Web, "web", "C:\\x\\Web\\SiagroB1.Web.exe", "Production", 55100)
        ];
        var order = new List<string>();

        var outcome = await InstallExecutor.RunAsync(
            [new StopServicesStep(services), new RecordingStep("Backup", order)], ctx, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Empty(order);
        Assert.Equal(new[] { "stop gw", "stop rep", "start gw" }, services.Calls);
    }

    [Fact]
    public async Task Run_RollbackErrorIsLoggedAndOthersStillRun()
    {
        var order = new List<string>();
        var log = new List<string>();
        var steps = new IInstallStep[] { new RecordingStep("A", order), new ThrowingRollback(), new RecordingStep("C", order, fail: true) };

        var outcome = await InstallExecutor.RunAsync(steps, Context("C:\\x", log), CancellationToken.None);

        Assert.Contains("rollback A", order);
        Assert.Contains(log, l => l.Contains("rollback quebrado"));
        Assert.False(outcome.Success);
    }

    private sealed class ThrowingRollback : IInstallStep
    {
        public string Title => "B";
        public Task ExecuteAsync(InstallContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(InstallContext ctx, CancellationToken ct) => throw new IOException("rollback quebrado");
    }
}

public class CopyFilesStepTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("siagro-copy").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private string P(params string[] parts) => Path.Combine([_root, .. parts]);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private InstallContext Ctx()
    {
        var ctx = InstallExecutorTests.Context(P("inst"), [], P("payload"));
        ctx.TargetServices =
        [
            new InstalledService(ServiceKind.Reports, "rep", P("inst", "Reports", "SiagroB1.Reports.exe"), "Production", 55200)
        ];
        return ctx;
    }

    [Fact]
    public async Task Copy_PreservesClientFiles()
    {
        Write(P("payload", "Reports", "SiagroB1.Reports.exe"), "novo");
        Write(P("payload", "Reports", "appsettings.json"), "{\"v\":2}");
        Write(P("payload", "Reports", "wwwroot", "images", "logo.png"), "logo-padrao");
        Write(P("inst", "Reports", "SiagroB1.Reports.exe"), "velho");
        Write(P("inst", "Reports", "appsettings.json"), "{\"v\":1}");
        Write(P("inst", "Reports", "appsettings.Production.json"), "cliente");
        Write(P("inst", "Reports", "logs", "app.log"), "log");
        Write(P("inst", "Reports", "wwwroot", "images", "logo.png"), "logo-cliente");

        await new CopyFilesStep().ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.Equal("novo", File.ReadAllText(P("inst", "Reports", "SiagroB1.Reports.exe")));
        Assert.Equal("{\"v\":2}", File.ReadAllText(P("inst", "Reports", "appsettings.json")));
        Assert.Equal("cliente", File.ReadAllText(P("inst", "Reports", "appsettings.Production.json")));
        Assert.Equal("log", File.ReadAllText(P("inst", "Reports", "logs", "app.log")));
        Assert.Equal("logo-cliente", File.ReadAllText(P("inst", "Reports", "wwwroot", "images", "logo.png")));
    }

    [Fact]
    public async Task Copy_RemovesStaleFiles()
    {
        Write(P("payload", "Reports", "SiagroB1.Reports.exe"), "novo");
        Write(P("inst", "Reports", "Antiga.dll"), "x");
        Write(P("inst", "Reports", "sub", "Velho.dll"), "x");

        await new CopyFilesStep().ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.False(File.Exists(P("inst", "Reports", "Antiga.dll")));
        Assert.False(File.Exists(P("inst", "Reports", "sub", "Velho.dll")));
    }

    [Fact]
    public async Task Rollback_RestoresBackup()
    {
        Write(P("payload", "Reports", "SiagroB1.Reports.exe"), "novo");
        Write(P("inst", "Reports", "SiagroB1.Reports.exe"), "velho");
        Write(P("inst", "Reports", "appsettings.Production.json"), "cliente");
        var ctx = Ctx();
        await new BackupStep(() => new DateTime(2026, 10, 27, 10, 0, 0)).ExecuteAsync(ctx, CancellationToken.None);
        var copy = new CopyFilesStep();
        await copy.ExecuteAsync(ctx, CancellationToken.None);
        File.WriteAllText(P("inst", "Reports", "appsettings.Production.json"), "mesclado");

        await copy.RollbackAsync(ctx, CancellationToken.None);

        Assert.Equal(P("inst", "backups", "20261027-100000"), ctx.BackupDir);
        Assert.Equal("velho", File.ReadAllText(P("inst", "Reports", "SiagroB1.Reports.exe")));
        Assert.Equal("cliente", File.ReadAllText(P("inst", "Reports", "appsettings.Production.json")));
    }

    [Fact]
    public async Task Rollback_NewInstall_RemovesCreatedFolder()
    {
        Write(P("payload", "Reports", "SiagroB1.Reports.exe"), "novo");
        var ctx = Ctx();
        var copy = new CopyFilesStep();
        await copy.ExecuteAsync(ctx, CancellationToken.None);

        await copy.RollbackAsync(ctx, CancellationToken.None);

        Assert.False(Directory.Exists(P("inst", "Reports")));
    }
}

public class WriteSettingsStepTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("siagro-settings").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Write_MergesClientFileAndCopiesLogo()
    {
        var reports = Path.Combine(_root, "Reports");
        Directory.CreateDirectory(reports);
        File.WriteAllText(Path.Combine(reports, "appsettings.json"), """{"CompanyName":"","Reports":{"InternalApiKey":""}}""");
        File.WriteAllText(Path.Combine(reports, "appsettings.Production.json"), """{"Extra":"mantido"}""");
        var logo = Path.Combine(_root, "logo-novo.png");
        File.WriteAllText(logo, "png");
        var log = new List<string>();
        var ctx = InstallExecutorTests.Context(_root, log);
        ctx.Settings.CompanyName = "ACME";
        ctx.Settings.ReportsInternalApiKey = "k";
        ctx.Settings.LogoSourcePath = logo;
        ctx.TargetServices = [new InstalledService(ServiceKind.Reports, "rep", Path.Combine(reports, "SiagroB1.Reports.exe"), "Production", 55200)];

        await new WriteSettingsStep().ExecuteAsync(ctx, CancellationToken.None);

        var saved = JsonFile.Load(Path.Combine(reports, "appsettings.Production.json"));
        Assert.Equal("ACME", saved["CompanyName"]!.GetValue<string>());
        Assert.Equal("mantido", saved["Extra"]!.GetValue<string>());
        Assert.Equal("png", File.ReadAllText(Path.Combine(reports, "wwwroot", "images", "logo.png")));
        Assert.Contains(log, l => l.Contains("possivelmente obsoleta") && l.Contains("Extra"));
        Assert.DoesNotContain(log, l => l.Contains("\"k\""));
    }
}

public class InstallPlanBuilderTests
{
    private static InstallContext Ctx(DetectionResult detection)
    {
        var ctx = new InstallContext(new InstallSettings(), detection, @"C:\IdxSistemas\SiagroB1\Producao", "Producao", "Production",
            @"C:\tmp\payload", "1.1.300", new SyncProgress([]));
        return ctx;
    }

    private static InstallDependencies Deps() =>
        new(new FakeServiceManager(), new MigrationRunner(new ProcessRunner(), new SqlCompanyLister(), new SqlMigrationHistory()));

    [Fact]
    public void Build_NewInstall_HasNoStopOrBackup()
    {
        var steps = InstallPlanBuilder.Build(Ctx(new DetectionResult(null, [], false)), Deps());

        Assert.DoesNotContain(steps, s => s is StopServicesStep or BackupStep);
        Assert.IsType<CopyFilesStep>(steps[0]);
    }

    [Fact]
    public void Build_Update_StartsWithStopAndBackup()
    {
        var existing = new InstalledService(ServiceKind.Web, "siagrob1webservice", @"C:\IdxSistemas\SiagroB1\Producao\Web\SiagroB1.Web.exe", "Production", 55100);

        var steps = InstallPlanBuilder.Build(Ctx(new DetectionResult(null, [existing], true)), Deps());

        Assert.IsType<StopServicesStep>(steps[0]);
        Assert.IsType<BackupStep>(steps[1]);
    }

    [Fact]
    public void ResolveServices_KeepsLegacyNamesAndCreatesNewOnes()
    {
        var existing = new InstalledService(ServiceKind.Web, "siagrob1webservice", @"C:\IdxSistemas\SiagroB1\Producao\Web\SiagroB1.Web.exe", "Production", 55100);
        var ctx = Ctx(new DetectionResult(null, [existing], true));
        ctx.Settings.GatewayPort = 55000;

        var services = InstallPlanBuilder.ResolveServices(ctx);

        Assert.Equal("siagrob1webservice", services.Single(s => s.Kind == ServiceKind.Web).ServiceName);
        var gateway = services.Single(s => s.Kind == ServiceKind.Gateway);
        Assert.Equal("siagrob1-producao-gateway", gateway.ServiceName);
        Assert.Equal(@"C:\IdxSistemas\SiagroB1\Producao\Gateway\SiagroB1.Gateway.exe", gateway.ExePath);
        Assert.Equal(55000, gateway.Port);
    }
}

public class MigrateStepTests
{
    [Fact]
    public async Task Migrate_WithoutDbBackupConfirmation_Throws()
    {
        var ctx = InstallExecutorTests.Context("C:\\x", []);
        ctx.MigrationReport = new MigrationReport([new PendingMigrations("Common", ["M1"])]);
        ctx.DatabaseBackupConfirmed = false;
        var step = new MigrateStep(new MigrationRunner(new ProcessRunner(), new SqlCompanyLister(), new SqlMigrationHistory()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(ctx, CancellationToken.None));

        Assert.Contains("backup do banco", ex.Message);
    }
}

public class ProbeStepTests
{
    [Fact]
    public async Task Probe_Failure_OnlyWarns()
    {
        var log = new List<string>();
        var ctx = InstallExecutorTests.Context("C:\\x", log);
        ctx.TargetServices = [new InstalledService(ServiceKind.Gateway, "gw", "C:\\x\\Gateway\\SiagroB1.Gateway.exe", "Production", 1)];

        await new ProbeStep(TimeSpan.FromMilliseconds(200)).ExecuteAsync(ctx, CancellationToken.None);

        Assert.Contains(log, l => l.Contains("não respondeu"));
        Assert.Single(ctx.Warnings);
    }
}
