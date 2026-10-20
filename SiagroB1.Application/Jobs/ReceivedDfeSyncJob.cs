using Hangfire;
using SiagroB1.Application.Services.Nfe;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// A cada 5 minutos confere quais filiais têm horário de consulta de DF-e vencido (agenda do usuário, hora de
/// Brasília) e consulta a SEFAZ. Sem retry do Hangfire: a próxima execução já é a retentativa.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class ReceivedDfeSyncJob(ReceivedDfeSyncService service)
{
    public const string CronExpression = "*/5 * * * *";

    public Task ExecuteAsync(CancellationToken ct = default) => service.RunScheduledAsync(DateTimeOffset.UtcNow, ct);
}
