using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Varre a fila de e-mail da NF-e e enfileira o envio das linhas AUTOMÁTICAS pendentes. É o mecanismo de entrega
/// (mesmo motivo do <see cref="ContractNotificationSweepJob"/>): a linha nasce no save da NF-e, e enfileirar
/// direto deixaria o worker ler antes do COMMIT. A carência de 1 minuto cobre esse COMMIT.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class NfeEmailSweepJob(
    AppDbContext context,
    IBackgroundJobClient backgroundJobClient,
    ICompanyContext companyContext,
    ILogger<NfeEmailSweepJob> logger)
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(1);

    private const int BatchSize = 200;

    /// <summary>
    /// Só reenfileira uma linha já enfileirada e nunca tentada depois deste prazo (enfileiramento perdido). Retentativa
    /// de envio falho é do Hangfire (30/120/600 s): linha com tentativa nunca volta pelo varredor.
    /// </summary>
    public static readonly TimeSpan ReenqueueAfter = TimeSpan.FromMinutes(30);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var threshold = now - GracePeriod;
        var staleEnqueue = now - ReenqueueAfter;

        var pending = await context.NfeEmailMessages
            .Where(m => m.Status == NfeEmailStatus.Pending && m.Trigger == NfeEmailTrigger.Automatic &&
                        m.AttemptCount == 0 && m.CreatedAt <= threshold &&
                        (m.EnqueuedAt == null || m.EnqueuedAt <= staleEnqueue))
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        var companyCode = companyContext.CompanyCode;

        foreach (var row in pending)
        {
            var key = row.Key;
            backgroundJobClient.Enqueue<NfeEmailDispatchJob>(job => job.ExecuteAsync(companyCode, key, CancellationToken.None));
            row.EnqueuedAt = now;
        }

        await context.SaveChangesAsync(ct);

        logger.LogInformation("{Count} e-mails de NF-e enfileirados para envio.", pending.Count);
    }
}
