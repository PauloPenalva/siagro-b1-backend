using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public sealed record ManualQuoteInput(
    decimal Last, decimal? Bid, decimal? Ask, decimal? High, decimal? Low, decimal? Open, bool IsSettlement);

public sealed record MarketQuoteView(
    string Code, string Description, string GroupName, int SortOrder, decimal? Last, decimal? Bid,
    decimal? ChangePercent, decimal? High, decimal? Low, decimal? Open, bool IsSettlement, DateTime? QuotedAt,
    MarketQuoteSource? Source, bool ManualOverride);

public class MarketQuoteService(AppDbContext context, ILogger<MarketQuoteService> logger)
{
    /// <summary>Instrumentos ativos com a última cotação (se houver), por grupo e ordem.</summary>
    public async Task<IReadOnlyList<MarketQuoteView>> GetBoardAsync(CancellationToken cancellationToken)
    {
        var rows = await context.MarketInstruments
            .AsNoTracking()
            .Where(i => i.Active)
            .GroupJoin(context.MarketQuotes.AsNoTracking(), i => i.Code, q => q.InstrumentCode,
                (i, qs) => new { Instrument = i, Quotes = qs })
            .SelectMany(x => x.Quotes.DefaultIfEmpty(), (x, q) => new { x.Instrument, Quote = q })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Instrument.GroupName, StringComparer.Ordinal)
            .ThenBy(r => r.Instrument.SortOrder)
            .Select(r => new MarketQuoteView(
                r.Instrument.Code, r.Instrument.Description, r.Instrument.GroupName ?? string.Empty,
                r.Instrument.SortOrder, r.Quote?.Last, r.Quote?.Bid, r.Quote?.ChangePercent, r.Quote?.High,
                r.Quote?.Low, r.Quote?.Open, r.Quote?.IsSettlement ?? false, r.Quote?.QuotedAt, r.Quote?.Source,
                r.Quote?.ManualOverride ?? false))
            .ToList();
    }

    /// <summary>
    /// Aplica o que veio da CMA. O stream manda deltas: campo nulo mantém o valor guardado.
    /// Símbolo desconhecido/inativo e instrumento com lançamento manual ativo são ignorados.
    /// </summary>
    public async Task ApplyCmaUpdatesAsync(IReadOnlyCollection<MarketQuoteUpdate> updates, CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
            return;

        var symbols = updates.Select(u => u.Symbol).Distinct().ToList();
        var active = (await context.MarketInstruments
                .Where(i => i.Active && symbols.Contains(i.Code))
                .Select(i => i.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var quotes = await context.MarketQuotes
            .Where(q => symbols.Contains(q.InstrumentCode))
            .ToDictionaryAsync(q => q.InstrumentCode, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var update in updates)
        {
            if (!active.Contains(update.Symbol))
                continue;

            if (!quotes.TryGetValue(update.Symbol, out var quote))
            {
                quote = new MarketQuote { InstrumentCode = update.Symbol };
                context.MarketQuotes.Add(quote);
                quotes[update.Symbol] = quote;
            }
            else if (quote.ManualOverride)
            {
                continue;
            }

            quote.Last = update.Last ?? quote.Last;
            quote.Bid = update.Bid ?? quote.Bid;
            quote.Ask = update.Ask ?? quote.Ask;
            quote.ChangePercent = update.ChangePercent ?? quote.ChangePercent;
            quote.High = update.High ?? quote.High;
            quote.Low = update.Low ?? quote.Low;
            quote.Open = update.Open ?? quote.Open;
            quote.PreviousClose = update.PreviousClose ?? quote.PreviousClose;
            if (update.Last.HasValue)
                quote.IsSettlement = update.IsSettlement;
            quote.QuotedAt = update.QuotedAt ?? now;
            quote.Source = MarketQuoteSource.Cma;
            quote.UserName = null;
            quote.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Lançamento manual: vale até ser liberado e a CMA não o sobrescreve.</summary>
    public async Task SetManualAsync(string instrumentCode, ManualQuoteInput input, string userName, CancellationToken cancellationToken)
    {
        var active = await context.MarketInstruments.AnyAsync(i => i.Code == instrumentCode && i.Active, cancellationToken);
        if (!active)
            throw new BusinessException($"Instrumento de mercado {instrumentCode} não encontrado ou inativo.");

        var quote = await context.MarketQuotes.FirstOrDefaultAsync(q => q.InstrumentCode == instrumentCode, cancellationToken);
        if (quote is null)
        {
            quote = new MarketQuote { InstrumentCode = instrumentCode };
            context.MarketQuotes.Add(quote);
        }

        var now = DateTime.UtcNow;
        quote.Last = input.Last;
        quote.Bid = input.Bid;
        quote.Ask = input.Ask;
        quote.High = input.High;
        quote.Low = input.Low;
        quote.Open = input.Open;
        quote.IsSettlement = input.IsSettlement;
        quote.Source = MarketQuoteSource.Manual;
        quote.UserName = userName;
        quote.ManualOverride = true;
        quote.QuotedAt = now;
        quote.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Cotação manual de {Code} lançada por {User}.", instrumentCode, userName);
    }

    /// <summary>Devolve o instrumento ao feed da CMA (a próxima atualização sobrescreve).</summary>
    public async Task ReleaseManualAsync(string instrumentCode, CancellationToken cancellationToken)
    {
        var quote = await context.MarketQuotes.FirstOrDefaultAsync(q => q.InstrumentCode == instrumentCode, cancellationToken);
        if (quote is null || !quote.ManualOverride)
            return;

        quote.ManualOverride = false;
        quote.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
