using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Reinf;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Jobs.Reinf;

/// <summary>
/// A cada 2 minutos consulta os lotes da EFD-Reinf ainda sem retorno (enviados há ≥ 1 min, menos de 30 tentativas).
/// Um lote com erro não impede os demais; sem retry do Hangfire (a próxima execução é a retentativa).
/// </summary>
[AutomaticRetry(Attempts = 0)]
[Queue(QueueName)]
public class ReinfBatchQueryJob(AppDbContext context, IReinfService service, TimeProvider time, ILogger<ReinfBatchQueryJob> logger)
{
    public const string QueueName = "reinf";
    public const string CronExpression = "*/2 * * * *";
    public const int MaxAttempts = 30;

    public Task ExecuteAsync(CancellationToken ct = default) => RunAsync(ct);

    public async Task RunAsync(CancellationToken ct)
    {
        var limit = time.GetUtcNow().UtcDateTime.AddMinutes(-1);
        var keys = await context.ReinfBatches.AsNoTracking()
            .Where(b => (b.Status == ReinfBatchStatus.Sent || b.Status == ReinfBatchStatus.Processing)
                        && b.Protocol != null && b.Protocol != "" && b.SentAt <= limit && b.Attempts < MaxAttempts)
            .OrderBy(b => b.SentAt)
            .Select(b => b.Key)
            .ToListAsync(ct);

        foreach (var key in keys)
        {
            try
            {
                await service.QueryBatchAsync(key, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao consultar o lote {BatchKey} da EFD-Reinf.", key);
                context.ChangeTracker.Clear();
                // Conta a tentativa falha para o lote parar nas 30.
                var batch = await context.ReinfBatches.FirstOrDefaultAsync(b => b.Key == key, ct);
                if (batch is not null)
                {
                    batch.Attempts++;
                    batch.Error = ex.Message;
                    await context.SaveChangesAsync(ct);
                }
            }
        }
    }
}
