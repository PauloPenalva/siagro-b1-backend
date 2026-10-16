using System.ComponentModel;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Jobs.Companies;

/// <summary>
/// Executa um job recorrente uma vez POR EMPRESA, cada vez num escopo novo com a empresa definida
/// (explícita no escopo e ambiente, para escopos aninhados como os do ContractDraftsReconcileJob).
/// Chave string em vez de método genérico: o painel do Hangfire mostra o nome legível e não depende
/// da serialização de genéricos.
/// </summary>
public sealed class CompanyScopedJobRunner(
    ICompanyCatalog catalog,
    IServiceScopeFactory scopeFactory,
    ILogger<CompanyScopedJobRunner> logger)
{
    public const string StorageDailyCalculation = "storage-daily-calculation";
    public const string ContractNotificationSweep = "contract-notification-sweep";
    public const string ContractDraftsReconcile = "contract-drafts-reconcile";

    private static readonly IReadOnlyDictionary<string, Func<IServiceProvider, CancellationToken, Task>> Jobs =
        new Dictionary<string, Func<IServiceProvider, CancellationToken, Task>>
        {
            [StorageDailyCalculation] = (sp, ct) => sp.GetRequiredService<IStorageAddressesDailyCalculationJob>().ExecuteAsync(null, ct),
            [ContractNotificationSweep] = (sp, ct) => sp.GetRequiredService<IContractNotificationSweepJob>().ExecuteAsync(ct),
            [ContractDraftsReconcile] = (sp, ct) => sp.GetRequiredService<ContractDraftsReconcileJob>().ExecuteAsync(ct),
        };

    /// <summary>
    /// Mesma execução, sem retry do Hangfire: para os jobs que já rodam a cada minuto/30 min, a
    /// próxima execução É a retentativa — com retry, uma empresa fora do ar multiplicaria as
    /// execuções das demais. O método é separado porque o Hangfire lê os atributos do MÉTODO invocado.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisplayName("{0} (todas as empresas)")]
    public Task RunForAllCompaniesWithoutRetryAsync(string jobKey, CancellationToken ct) => RunForAllCompaniesAsync(jobKey, ct);

    [DisplayName("{0} (todas as empresas)")]
    public async Task RunForAllCompaniesAsync(string jobKey, CancellationToken ct)
    {
        if (!Jobs.TryGetValue(jobKey, out var run))
            throw new ArgumentOutOfRangeException(nameof(jobKey), jobKey, "Job desconhecido para o runner por empresa.");

        var failures = new List<(string Company, Exception Error)>();

        foreach (var company in await catalog.GetAllAsync(ct))
        {
            try
            {
                using var _ = CompanyAmbient.Use(company.Code);
                using var scope = scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);

                await run(scope.ServiceProvider, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Uma empresa com problema não pode parar as outras — mas o job termina falho.
                logger.LogError(e, "Job {Job} falhou na empresa {Company}", jobKey, company.Code);
                failures.Add((company.Code, e));
            }
        }

        if (failures.Count > 0)
            throw new AggregateException(
                $"Job {jobKey} falhou em {failures.Count} empresa(s): {string.Join(", ", failures.Select(f => $"{f.Company}: {f.Error.Message}"))}", failures.Select(f => f.Error));
    }
}
