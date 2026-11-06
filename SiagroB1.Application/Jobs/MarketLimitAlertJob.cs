using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Commons.Time;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Confere os limites de exposição a cada 15 min no horário comercial (dias úteis) e avisa os estouros
/// (um aviso por limite por dia). A data é a LOCAL de São Paulo.
/// </summary>
public class MarketLimitAlertJob(MarketLimitAlertService service, TimeProvider time, ILogger<MarketLimitAlertJob> logger)
{
    public const string Cron = "*/15 8-19 * * 1-5";

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), SaoPauloTime.Zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var written = await service.CheckAsync(date, ct);
        if (written > 0)
            logger.LogInformation("Limites de exposição em {Date}: {Count} avisos novos.", date, written);
    }
}
