using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs.Companies;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Tests.Jobs;

/// <summary>
/// Um job recorrente roda uma vez POR EMPRESA, cada vez num escopo com a empresa definida. A falha
/// de uma empresa não impede as outras, mas o job termina falho para o Hangfire registrar.
/// </summary>
public class CompanyScopedJobRunnerTests
{
    private static readonly CompanyInfo A = new("0001", "A", "Server=.;Database=A");
    private static readonly CompanyInfo B = new("0002", "B", "Server=.;Database=B");

    /// <summary>Job de teste: anota a empresa do escopo em que rodou e, opcionalmente, falha numa delas.</summary>
    private sealed class ProbeJob(ICompanyContext companyContext, List<string> seen, string? failOn) : IStorageAddressesDailyCalculationJob
    {
        public Task ExecuteAsync(DateTime? processingDate = null, CancellationToken ct = default)
        {
            seen.Add($"{companyContext.CompanyCode}:{CompanyAmbient.CompanyCode}");
            if (companyContext.CompanyCode == failOn) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }
    }

    private static (CompanyScopedJobRunner Runner, List<string> Seen) Build(string? failOn = null)
    {
        var seen = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TestConfiguration.With(("ConnectionStrings:SiagroDB", "Server=.;Database=FALLBACK")));
        services.AddSingleton<ICompanyCatalog>(new FakeCompanyCatalog(A, B));
        services.AddScoped<ICompanyContext, CompanyContext>();
        services.AddScoped<IStorageAddressesDailyCalculationJob>(sp => new ProbeJob(sp.GetRequiredService<ICompanyContext>(), seen, failOn));
        services.AddScoped<CompanyScopedJobRunner>();
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<CompanyScopedJobRunner>(), seen);
    }

    [Fact]
    public async Task Runs_the_job_once_per_company_with_the_company_set_in_scope_and_ambient()
    {
        var (runner, seen) = Build();

        await runner.RunForAllCompaniesAsync(CompanyScopedJobRunner.StorageDailyCalculation, CancellationToken.None);

        Assert.Equal(["0001:0001", "0002:0002"], seen);
        Assert.Null(CompanyAmbient.CompanyCode);   // restaurado
    }

    [Fact]
    public async Task A_failing_company_does_not_stop_the_others_but_the_job_fails()
    {
        var (runner, seen) = Build(failOn: "0001");

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            runner.RunForAllCompaniesAsync(CompanyScopedJobRunner.StorageDailyCalculation, CancellationToken.None));

        Assert.Equal(2, seen.Count);
        Assert.Contains("0001", ex.Message);
    }

    [Fact]
    public async Task Unknown_job_key_throws()
    {
        var (runner, _) = Build();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunForAllCompaniesAsync("nao-existe", CancellationToken.None));
    }
}
