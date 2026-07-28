using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Varre a outbox e enfileira o envio das notificações pendentes.
///
/// É o mecanismo de ENTREGA, não uma rede de segurança. Enfileirar direto no serviço de
/// mutação não funcionaria: o <c>IBackgroundJobClient</c> grava em conexão própria com
/// autocommit, então o worker poderia ler a outbox antes do COMMIT da transação de negócio,
/// não encontrar a linha e desistir em silêncio — a notificação some sem deixar rastro.
/// Varrendo, só se enxerga o que já está commitado.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class ContractNotificationSweepJob(
    AppDbContext context,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ContractNotificationSweepJob> logger) : IContractNotificationSweepJob
{
    /// <summary>
    /// Carência para o COMMIT da transação de negócio acontecer. Sem ela, uma linha recém-criada
    /// seria enfileirada ainda dentro da transação do contrato.
    /// </summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Teto por rodada. Um backlog grande (feature religada depois de dias desligada) não pode
    /// virar uma rajada única — é assim que o número da empresa é banido no provedor.
    /// </summary>
    private const int BatchSize = 200;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.Now - GracePeriod;

        var pending = await context.NotificationOutboxMessages
            .AsNoTracking()
            .Where(m => m.Status == NotificationOutboxStatus.Pending && m.OccurredAt < threshold)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .Select(m => m.Key)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var outboxKey in pending)
        {
            // Reenfileirar uma linha já enfileirada é inofensivo: o job de envio só age em
            // Pending, então a segunda execução sai sem fazer nada.
            backgroundJobClient.Enqueue<IContractNotificationDispatchJob>(
                job => job.ExecuteAsync(outboxKey, CancellationToken.None));
        }

        logger.LogInformation("{Count} notificações enfileiradas para envio.", pending.Count);
    }
}
