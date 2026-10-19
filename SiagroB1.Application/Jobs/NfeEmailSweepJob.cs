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

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.Now - GracePeriod;

        var pending = await context.NfeEmailMessages
            .AsNoTracking()
            .Where(m => m.Status == NfeEmailStatus.Pending && m.Trigger == NfeEmailTrigger.Automatic &&
                        m.CreatedAt <= threshold)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .Select(m => m.Key)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        var companyCode = companyContext.CompanyCode;

        foreach (var key in pending)
            backgroundJobClient.Enqueue<NfeEmailDispatchJob>(job => job.ExecuteAsync(companyCode, key, CancellationToken.None));

        logger.LogInformation("{Count} e-mails de NF-e enfileirados para envio.", pending.Count);
    }
}
