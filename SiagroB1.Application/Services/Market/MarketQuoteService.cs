using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public sealed record ManualQuoteInput(
    decimal Last, decimal? Bid, decimal? Ask, decimal? High, decimal? Low, decimal? Open, bool IsSettlement);

public sealed record MarketQuoteView(
    string Code, string Description, string GroupName, int SortOrder, decimal? Last, decimal? Bid,
    decimal? ChangePercent, decimal? High, decimal? Low, decimal? Open, bool IsSettlement, DateTime? QuotedAt,
    MarketQuoteSource? Source, bool ManualOverride, decimal? Ask = null, string? UserName = null);

public class MarketQuoteService(
    AppDbContext context, LiveQuoteStore store, ICompanyContext companyContext, ILogger<MarketQuoteService> logger)
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

        // Abas na ordem do operador: grupo pelo menor SortOrder dele, depois nome; dentro, SortOrder e código.
        var groupOrder = rows
            .GroupBy(r => r.Instrument.GroupName ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Min(r => r.Instrument.SortOrder));

        return rows
            .OrderBy(r => groupOrder[r.Instrument.GroupName ?? string.Empty])
            .ThenBy(r => r.Instrument.GroupName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(r => r.Instrument.SortOrder)
            .ThenBy(r => r.Instrument.Code, StringComparer.Ordinal)
            .Select(r => new MarketQuoteView(
                r.Instrument.Code, r.Instrument.Description, r.Instrument.GroupName ?? string.Empty,
                r.Instrument.SortOrder, r.Quote?.Last, r.Quote?.Bid, r.Quote?.ChangePercent, r.Quote?.High,
                r.Quote?.Low, r.Quote?.Open, r.Quote?.IsSettlement ?? false, AsUtc(r.Quote?.QuotedAt), r.Quote?.Source,
                r.Quote?.ManualOverride ?? false, r.Quote?.Ask, r.Quote?.UserName))
            .ToList();
    }

    /// <summary>
    /// Gravado em UTC num datetime2, o EF devolve Kind=Unspecified e o JSON sairia sem "Z" (a tela somava 3 h).
    /// </summary>
    internal static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    /// <summary>
    /// Aplica o que veio da CMA. O stream manda deltas: campo nulo mantém o valor guardado.
    /// Símbolo desconhecido/inativo e instrumento com lançamento manual ativo são ignorados.
    /// </summary>
    public async Task ApplyCmaUpdatesAsync(IReadOnlyCollection<MarketQuoteUpdate> updates, CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
            return;

        try
        {
            await ApplyCmaUpdatesOnceAsync(updates, cancellationToken);
        }
        catch (DbUpdateException e)
        {
            // Lançamento manual gravado entre a leitura e o SaveChanges (token de concorrência em ManualOverride)
            // ou primeira cotação inserida em paralelo (chave duplicada): relê e reaplica uma vez, respeitando o manual.
            logger.LogInformation("Conflito ao gravar cotações da CMA ({Type}); reaplicando sobre o estado atual.", e.GetType().Name);
            DetachQuotes();

            try
            {
                await ApplyCmaUpdatesOnceAsync(updates, cancellationToken);
            }
            catch (DbUpdateException retry)
            {
                DetachQuotes();
                logger.LogWarning("Cotações da CMA descartadas após novo conflito ({Type}); o próximo lote as reenviará.",
                    retry.GetType().Name);
            }
        }
    }

    private void DetachQuotes()
    {
        foreach (var entry in context.ChangeTracker.Entries<MarketQuote>().ToList())
            entry.State = EntityState.Detached;
    }

    private async Task ApplyCmaUpdatesOnceAsync(IReadOnlyCollection<MarketQuoteUpdate> updates, CancellationToken cancellationToken)
    {
        var symbols = updates.Select(u => u.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Código canônico do cadastro por símbolo, sem depender da collation do banco.
        var instruments = (await context.MarketInstruments
                .Where(i => i.Active)
                .Select(i => i.Code)
                .ToListAsync(cancellationToken))
            .Where(code => symbols.Contains(code, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(code => code, StringComparer.OrdinalIgnoreCase);
        var codes = instruments.Values.ToList();
        var quotes = (await context.MarketQuotes
                .Where(q => codes.Contains(q.InstrumentCode))
                .ToListAsync(cancellationToken))
            .ToDictionary(q => q.InstrumentCode, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var update in updates)
        {
            if (!instruments.TryGetValue(update.Symbol, out var code))
                continue;

            if (!quotes.TryGetValue(code, out var quote))
            {
                quote = new MarketQuote { InstrumentCode = code };
                context.MarketQuotes.Add(quote);
                quotes[code] = quote;
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
            {
                // Só o preço muda a autoria: um delta só de Bid depois de "Liberar" não rotula o manual como CMA.
                quote.IsSettlement = update.IsSettlement;
                quote.Source = MarketQuoteSource.Cma;
                quote.UserName = null;
            }
            quote.QuotedAt = update.QuotedAt ?? now;
            quote.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Lançamento manual: vale até ser liberado e a CMA não o sobrescreve.</summary>
    public async Task SetManualAsync(string instrumentCode, ManualQuoteInput input, string userName, CancellationToken cancellationToken)
    {
        if (input.Last <= 0)
            throw new BusinessException("Informe o último preço (maior que zero).");
        if (new[] { input.Bid, input.Ask, input.High, input.Low, input.Open }.Any(v => v < 0))
            throw new BusinessException("Preços não podem ser negativos.");

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
        store.NotifyChanged(companyContext.CompanyCode);
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
        store.NotifyChanged(companyContext.CompanyCode);
    }
}
