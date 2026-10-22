using System.ComponentModel;
using Hangfire;
using SiagroB1.Application.Services.Accounting.Offline;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Contabilização off-line de UMA empresa, disparada pela tela. O <c>companyCode</c> como primeiro parâmetro é a
/// convenção do <see cref="Companies.CompanyJobFilter"/>, que define a empresa antes de o escopo de DI abrir.
/// Sem retry: rodar de novo é pedir outra execução pela tela.
/// </summary>
public class OfflinePostingJob(IOfflinePostingService service)
{
    [AutomaticRetry(Attempts = 0)]
    [DisplayName("Contabilização off-line {0}")]
    public Task ExecuteAsync(string companyCode, Guid runKey, CancellationToken ct) => service.ExecuteAsync(runKey, ct);
}
