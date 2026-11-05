using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public class MarketDailyCloseService(AppDbContext context, ILogger<MarketDailyCloseService> logger)
{
    /// <summary>
    /// Fecha o dia: um registro por instrumento ativo com cotação. Precedência: Manual, depois ajuste
    /// (settlement), depois último. Fechamento manual existente nunca é sobrescrito. Devolve quantos gravou.
    /// </summary>
    public async Task<int> CloseDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        var quotes = await context.MarketQuotes
            .Where(q => q.Last != null && context.MarketInstruments.Any(i => i.Code == q.InstrumentCode && i.Active))
            .ToListAsync(cancellationToken);
        var existing = await context.MarketDailyCloses
            .Where(c => c.Date == day)
            .ToDictionaryAsync(c => c.InstrumentCode, cancellationToken);

        var now = DateTime.UtcNow;
        var written = 0;
        foreach (var quote in quotes)
        {
            if (existing.TryGetValue(quote.InstrumentCode, out var close))
            {
                if (close.Source == MarketQuoteSource.Manual)
                    continue;
            }
            else
            {
                close = new MarketDailyClose { InstrumentCode = quote.InstrumentCode, Date = day, CreatedAt = now };
                context.MarketDailyCloses.Add(close);
            }

            var manual = quote.Source == MarketQuoteSource.Manual;
            close.Close = quote.Last!.Value;
            close.Source = manual ? MarketQuoteSource.Manual : MarketQuoteSource.Cma;
            close.IsSettlement = !manual && quote.IsSettlement;
            close.UserName = manual ? quote.UserName : null;
            close.UpdatedAt = now;
            written++;
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Fechamento de mercado de {Date}: {Count} registros.", date, written);
        return written;
    }

    public async Task SetManualCloseAsync(string code, DateOnly date, decimal close, string userName, CancellationToken cancellationToken)
    {
        if (!await context.MarketInstruments.AnyAsync(i => i.Code == code, cancellationToken))
            throw new BusinessException($"Instrumento de mercado {code} não encontrado.");

        var day = date.ToDateTime(TimeOnly.MinValue);
        var now = DateTime.UtcNow;
        var row = await context.MarketDailyCloses
            .FirstOrDefaultAsync(c => c.InstrumentCode == code && c.Date == day, cancellationToken);
        if (row is null)
        {
            row = new MarketDailyClose { InstrumentCode = code, Date = day, CreatedAt = now };
            context.MarketDailyCloses.Add(row);
        }

        row.Close = close;
        row.Source = MarketQuoteSource.Manual;
        row.IsSettlement = false;
        row.UserName = userName;
        row.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
    }
}
