using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Market;
using SiagroB1.Commons.Time;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Fechamento diário das cotações às 18h de Brasília (dias úteis): grava a última cotação de cada instrumento
/// ativo como fechamento do dia, que alimenta a marcação a mercado. A data é a LOCAL de São Paulo, não a UTC
/// (21h UTC ainda é o mesmo dia; 01h UTC já seria o dia seguinte).
/// </summary>
public class MarketDailyCloseJob(MarketDailyCloseService service, TimeProvider time, ILogger<MarketDailyCloseJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var date = SaoPauloTime.Today(time);
        var written = await service.CloseDayAsync(date, ct);

        logger.LogInformation("Fechamento das cotações de {Date}: {Count} instrumentos gravados.", date, written);
    }
}
