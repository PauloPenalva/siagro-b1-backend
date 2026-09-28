using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Rede de segurança do webhook: se um evento do D4Sign se perder (URL fora do ar, sete tentativas
/// esgotadas), a minuta ficaria parada para sempre. A cada 30 min, olha as que não mudam há mais de
/// 6 h e busca a situação no provedor.
///
/// Sem retry do Hangfire: a próxima execução já é a retentativa, e uma falha do provedor não
/// melhora em segundos.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class ContractDraftsReconcileJob(
    AppDbContext context,
    ContractDraftsRefreshStateService refreshState,
    ILogger<ContractDraftsReconcileJob> logger)
{
    public const string RecurringJobId = "contract-drafts-reconcile";
    public const string CronExpression = "*/30 * * * *";

    private const int BatchSize = 50;
    private static readonly TimeSpan Staleness = TimeSpan.FromHours(6);

    /// <returns>Quantas minutas mudaram de estado.</returns>
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.Now - Staleness;

        // Duas populações: as que ainda correm, e as que ficaram assinadas SEM o PDF assinado —
        // sem a segunda, um download que falhou uma vez nunca mais é tentado.
        var keys = await context.ContractDrafts.AsNoTracking()
            .Where(d => (d.Status == ContractDraftStatus.AwaitingSignature
                         || d.Status == ContractDraftStatus.PartiallySigned
                         || (d.Status == ContractDraftStatus.Signed && d.SignedAttachmentKey == null))
                        && d.ExternalDocumentId != null
                        && d.UpdatedAt < cutoff)
            .OrderBy(d => d.UpdatedAt)
            .Take(BatchSize)
            .Select(d => d.Key)
            .ToListAsync(ct);

        if (keys.Count == 0) return 0;

        var changed = 0;
        foreach (var key in keys)
        {
            try
            {
                if (await refreshState.ExecuteAsync(key, "reconciliacao", ct)) changed++;
            }
            catch (Exception e)
            {
                // Uma minuta problemática não pode parar as outras 49.
                logger.LogError(e, "Falha ao reconciliar a minuta {Key}", key);
            }
        }

        logger.LogInformation("Reconciliação de minutas: {Changed} de {Total} atualizadas", changed, keys.Count);
        return changed;
    }
}
