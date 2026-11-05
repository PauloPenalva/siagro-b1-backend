using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Time;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public class MarketDailyCloseService(
    AppDbContext context, ILogger<MarketDailyCloseService> logger, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // Precedência do fechamento: nunca rebaixar (retry/misfire do job não troca ajuste por último).
    private static int Rank(MarketQuoteSource source, bool isSettlement) =>
        source == MarketQuoteSource.Manual ? 3 : isSettlement ? 2 : 1;

    /// <summary>
    /// Fecha o dia: um registro por instrumento ativo com cotação. Precedência: Manual, depois ajuste
    /// (settlement), depois último. Fechamento manual existente nunca é sobrescrito, e um existente só é
    /// substituído por candidato de nível igual ou maior. Devolve quantos gravou.
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
            var manual = quote.Source == MarketQuoteSource.Manual;
            var settlement = !manual && quote.IsSettlement;
            if (existing.TryGetValue(quote.InstrumentCode, out var close))
            {
                if (close.Source == MarketQuoteSource.Manual)
                    continue;
                if (Rank(manual ? MarketQuoteSource.Manual : MarketQuoteSource.Cma, settlement) < Rank(close.Source, close.IsSettlement))
                    continue;
            }
            else
            {
                close = new MarketDailyClose { InstrumentCode = quote.InstrumentCode, Date = day, CreatedAt = now };
                context.MarketDailyCloses.Add(close);
            }

            close.Close = quote.Last!.Value;
            close.Source = manual ? MarketQuoteSource.Manual : MarketQuoteSource.Cma;
            close.IsSettlement = settlement;
            close.QuotedAt = quote.QuotedAt == default ? null : DateTime.SpecifyKind(quote.QuotedAt, DateTimeKind.Utc);
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
        if (close <= 0)
            throw new BusinessException("O fechamento deve ser maior que zero.");
        if (date > SaoPauloTime.Today(_time))
            throw new BusinessException("A data do fechamento não pode ser futura.");

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
        row.QuotedAt = null;
        row.UserName = userName;
        row.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
    }
}
