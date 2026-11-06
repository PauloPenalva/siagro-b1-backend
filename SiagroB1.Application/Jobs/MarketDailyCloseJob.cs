using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Market;
using SiagroB1.Commons.Time;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Fechamento diário das cotações às 18h de Brasília (dias úteis): grava a última cotação de cada instrumento
/// ativo como fechamento do dia, que alimenta a marcação a mercado. A data é a LOCAL de São Paulo, não a UTC
/// (21h UTC ainda é o mesmo dia; 01h UTC já seria o dia seguinte). Só fecha entre 18:00:00 e 23:59:59 locais:
/// um misfire na manhã seguinte gravaria o preço de hoje como fechamento de ontem.
/// </summary>
public class MarketDailyCloseJob(MarketDailyCloseService service, TimeProvider time, ILogger<MarketDailyCloseJob> logger)
{
    /// <summary>Hora local (São Paulo) em que a janela abre; o cron do Hangfire usa a mesma hora.</summary>
    public const int WindowStartHour = 18;

    public static string Cron => $"0 {WindowStartHour} * * 1-5";

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), SaoPauloTime.Zone);
        if (local.Hour < WindowStartHour)
        {
            logger.LogWarning("Fechamento das cotações fora da janela ({Local:yyyy-MM-dd HH:mm} em Brasília); nada gravado — use o lançamento manual.",
                local);
            return;
        }

        var date = DateOnly.FromDateTime(local.DateTime);
        var written = await service.CloseDayAsync(date, ct);

        logger.LogInformation("Fechamento das cotações de {Date}: {Count} instrumentos gravados.", date, written);
    }
}
