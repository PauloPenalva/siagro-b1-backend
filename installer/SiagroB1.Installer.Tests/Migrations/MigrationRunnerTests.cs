using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Services;

namespace SiagroB1.Installer.Tests.Migrations;

public class MigrationRunnerTests : IDisposable
{
    private const string CommonCs = "Data Source=srv;Initial Catalog=COMMON;User ID=idx;Password=segredo1";
    private const string DefaultCs = "Data Source=srv;Initial Catalog=EMP_PADRAO;User ID=idx;Password=segredo1";

    private readonly string _bundleDir = Directory.CreateTempSubdirectory("siagro-bundles").FullName;

    public MigrationRunnerTests()
    {
        File.WriteAllLines(Path.Combine(_bundleDir, MigrationRunner.CommonListFile), ["M1_Common", "M2_Common"]);
        File.WriteAllLines(Path.Combine(_bundleDir, MigrationRunner.AppListFile), ["A1", "A2", "A3"]);
    }

    public void Dispose() => Directory.Delete(_bundleDir, true);

    private sealed class FakeRunner(Func<string, int>? exitFor = null) : IProcessRunner
    {
        public List<(string File, IReadOnlyList<string> Args, string? Dir, IDictionary<string, string>? Env)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? workingDir, IDictionary<string, string>? env, CancellationToken ct)
        {
            Calls.Add((file, args, workingDir, env));
            var cs = args[args.ToList().IndexOf("--connection") + 1];
            var exit = exitFor?.Invoke(cs) ?? 0;
            return Task.FromResult(new ProcessResult(exit, exit == 0 ? "Done." : $"Login failed. Connection: {cs}"));
        }
    }

    private sealed class FakeCompanies(params CompanyDatabase[] companies) : ICompanyLister
    {
        public Task<IReadOnlyList<CompanyDatabase>> ListAsync(string commonConnectionString, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CompanyDatabase>>(companies);
    }

    private sealed class FakeHistory(Dictionary<string, string[]> applied) : IMigrationHistory
    {
        public Task<IReadOnlySet<string>> AppliedAsync(string connectionString, CancellationToken ct)
        {
            var key = applied.Keys.FirstOrDefault(k => connectionString.Contains(k));
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(key is null ? [] : applied[key]));
        }
    }

    private static CompanyDatabase Company(string code) =>
        new(code, $"Empresa {code}", $"Data Source=srv;Initial Catalog=EMP_{code};User ID=idx;Password=segredo1");

    private static MigrationRunner Runner(IProcessRunner runner, ICompanyLister companies, IMigrationHistory? history = null) =>
        new(runner, companies, history ?? new FakeHistory([]));

    private List<string> Log(out IProgress<string> progress)
    {
        var lines = new List<string>();
        progress = new SyncProgress(lines.Add);
        return lines;
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private MigrationRequest Request(string? ddlUser = null, string? ddlPassword = null) =>
        new(_bundleDir, "Production", CommonCs, DefaultCs, ddlUser, ddlPassword);

    [Fact]
    public async Task Apply_RunsCommonThenEachCompany()
    {
        var runner = new FakeRunner();
        var lines = Log(out var log);

        await Runner(runner, new FakeCompanies(Company("01"), Company("02"))).ApplyAsync(Request(), log, CancellationToken.None);

        Assert.Equal(3, runner.Calls.Count);
        Assert.EndsWith(MigrationRunner.CommonBundle, runner.Calls[0].File);
        Assert.Contains("Initial Catalog=COMMON", runner.Calls[0].Args[1]);
        Assert.EndsWith(MigrationRunner.AppBundle, runner.Calls[1].File);
        Assert.Contains("EMP_01", runner.Calls[1].Args[1]);
        Assert.Contains("EMP_02", runner.Calls[2].Args[1]);
        Assert.All(runner.Calls, c => Assert.Equal(_bundleDir, c.Dir));
        Assert.All(runner.Calls, c => Assert.Equal(c.Args[1], c.Env!["ConnectionStrings__SiagroDB"]));
        Assert.All(runner.Calls, c => Assert.Equal(c.Args[1], c.Env!["ConnectionStrings__SiagroCommon"]));
        Assert.All(runner.Calls, c => Assert.Equal("Production", c.Env!["ASPNETCORE_ENVIRONMENT"]));
    }

    [Fact]
    public async Task Apply_NoCompanies_UsesSiagroDb()
    {
        var runner = new FakeRunner();
        Log(out var log);

        await Runner(runner, new FakeCompanies()).ApplyAsync(Request(), log, CancellationToken.None);

        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains("EMP_PADRAO", runner.Calls[1].Args[1]);
    }

    [Fact]
    public async Task Apply_DdlUserReplacesCredentials()
    {
        var runner = new FakeRunner();
        Log(out var log);

        await Runner(runner, new FakeCompanies(Company("01"))).ApplyAsync(Request("sa", "ddlpwd"), log, CancellationToken.None);

        Assert.All(runner.Calls, c =>
        {
            Assert.Contains("User ID=sa", c.Args[1]);
            Assert.Contains("Password=ddlpwd", c.Args[1]);
        });
    }

    [Fact]
    public async Task Apply_StopsOnFirstFailure_NamesCompany()
    {
        var runner = new FakeRunner(cs => cs.Contains("EMP_02") ? 1 : 0);
        var lines = Log(out var log);

        var ex = await Assert.ThrowsAsync<MigrationFailedException>(() =>
            Runner(runner, new FakeCompanies(Company("01"), Company("02"), Company("03"))).ApplyAsync(Request(), log, CancellationToken.None));

        Assert.Equal("Empresa 02", ex.Label);
        Assert.Equal(3, runner.Calls.Count);
        Assert.Contains(lines, l => l.Contains("Empresa 01") && l.Contains("aplicad"));
        Assert.DoesNotContain("segredo1", ex.Message);
    }

    [Fact]
    public async Task Apply_LogNeverContainsPassword()
    {
        var runner = new FakeRunner(cs => cs.Contains("EMP_01") ? 1 : 0);
        var lines = Log(out var log);

        await Assert.ThrowsAsync<MigrationFailedException>(() =>
            Runner(runner, new FakeCompanies(Company("01"))).ApplyAsync(Request("sa", "ddlpwd"), log, CancellationToken.None));

        Assert.DoesNotContain(lines, l => l.Contains("segredo1") || l.Contains("ddlpwd"));
    }

    [Fact]
    public async Task ListPending_DiffsHistory()
    {
        var history = new FakeHistory(new()
        {
            ["COMMON"] = ["M1_Common"],
            ["EMP_01"] = ["A1", "A2", "A3"],
            ["EMP_02"] = ["A1"]
        });

        var report = await Runner(new FakeRunner(), new FakeCompanies(Company("01"), Company("02")), history)
            .ListPendingAsync(Request(), CancellationToken.None);

        Assert.Equal(["M2_Common"], report.Pending.Single(p => p.Label == "Common").Migrations);
        Assert.Empty(report.Pending.Single(p => p.Label == "Empresa 01").Migrations);
        Assert.Equal(["A2", "A3"], report.Pending.Single(p => p.Label == "Empresa 02").Migrations);
        Assert.True(report.HasPending);
    }
}
