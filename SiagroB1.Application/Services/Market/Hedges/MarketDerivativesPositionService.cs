using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

public sealed record MarketFuturesOpenPosition(int AccountId, string AccountLabel, string InstrumentCode, string InstrumentDescription,
    string Commodity, DateOnly? ExpirationDate, MarketQuoteUnit QuoteUnit, int NetLots, decimal AvgPrice, decimal? Tons, decimal? Usd,
    decimal? LastPrice, decimal? UnrealizedPnl, CurrencyType Currency);

public sealed record MarketRealizedRow(int MatchId, DateOnly CloseDate, int AccountId, string AccountLabel, string InstrumentCode,
    int Lots, decimal OpenPrice, decimal ClosePrice, decimal RealizedPnl, CurrencyType Currency);

/// <summary>
/// Posição de derivativos (informativa): lotes abertos por conta × instrumento e resultado realizado dos casamentos.
/// Número fixo de consultas (sem N+1).
/// </summary>
public class MarketDerivativesPositionService(AppDbContext context)
{
    public async Task<IReadOnlyList<MarketFuturesOpenPosition>> GetOpenPositionsAsync(CancellationToken ct)
    {
        var open = await context.MarketFuturesTrades.AsNoTracking().Where(t => t.OpenLots > 0)
            .Select(t => new { t.AccountId, t.InstrumentCode, t.Side, t.OpenLots, t.Price }).ToListAsync(ct);
        if (open.Count == 0)
            return [];

        var accountIds = open.Select(t => t.AccountId).Distinct().ToList();
        var codes = open.Select(t => t.InstrumentCode).Distinct().ToList();
        var labels = await LabelsAsync(accountIds, ct);
        var instruments = await context.MarketInstruments.AsNoTracking().Where(i => codes.Contains(i.Code))
            .ToDictionaryAsync(i => i.Code, StringComparer.OrdinalIgnoreCase, ct);
        var quotes = await context.MarketQuotes.AsNoTracking().Where(q => codes.Contains(q.InstrumentCode))
            .ToDictionaryAsync(q => q.InstrumentCode, q => q.Last, StringComparer.OrdinalIgnoreCase, ct);

        var rows = new List<MarketFuturesOpenPosition>();
        foreach (var g in open.GroupBy(t => (t.AccountId, Code: t.InstrumentCode.ToUpperInvariant())))
        {
            var net = g.Sum(t => t.Side == MarketTradeSide.Buy ? t.OpenLots : -t.OpenLots);
            if (net == 0 || !instruments.TryGetValue(g.Key.Code, out var instrument))
                continue;
            var avg = g.Sum(t => t.OpenLots * t.Price) / g.Sum(t => t.OpenLots);
            var unit = instrument.QuoteUnit;
            var tonsPerLot = MarketUnits.TonsPerLot(unit, instrument.ContractSize, instrument.TonFactor);
            var usdPerLot = MarketUnits.UsdPerLot(unit, instrument.ContractSize);
            var last = quotes.GetValueOrDefault(instrument.Code);
            decimal? unrealized = last is { } l
                ? Math.Round((l - avg) * net * MarketUnits.PointValue(unit, instrument.ContractSize), 2, MidpointRounding.AwayFromZero)
                : null;
            rows.Add(new MarketFuturesOpenPosition(
                g.Key.AccountId, labels.GetValueOrDefault(g.Key.AccountId, string.Empty), instrument.Code, instrument.Description,
                instrument.Commodity, instrument.ExpirationDate is { } e ? DateOnly.FromDateTime(e) : null, unit, net,
                Math.Round(avg, 6, MidpointRounding.AwayFromZero),
                tonsPerLot is { } t ? Math.Round(t * net, 4, MidpointRounding.AwayFromZero) : null,
                usdPerLot is { } u ? u * net : null,
                last, unrealized, MarketUnits.QuoteCurrency(unit)));
        }

        return rows.OrderBy(r => r.AccountLabel).ThenBy(r => r.InstrumentCode).ToList();
    }

    public async Task<IReadOnlyList<MarketRealizedRow>> GetRealizedAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (from > to)
            throw new BusinessException("A data inicial não pode ser maior que a final.");
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MinValue);
        var matches = await context.MarketFuturesMatches.AsNoTracking()
            .Where(m => m.CloseDate >= start && m.CloseDate <= end)
            .OrderBy(m => m.CloseDate).ThenBy(m => m.Id).ToListAsync(ct);
        var labels = await LabelsAsync(matches.Select(m => m.AccountId).Distinct().ToList(), ct);
        return matches.Select(m => new MarketRealizedRow(m.Id, DateOnly.FromDateTime(m.CloseDate), m.AccountId,
            labels.GetValueOrDefault(m.AccountId, string.Empty), m.InstrumentCode, m.Lots, m.OpenPrice, m.ClosePrice,
            m.RealizedPnl, m.Currency)).ToList();
    }

    /// <summary>"Corretora – conta".</summary>
    private async Task<Dictionary<int, string>> LabelsAsync(List<int> accountIds, CancellationToken ct) =>
        await context.MarketCounterpartyAccounts.AsNoTracking().Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, Name = a.Counterparty!.Name, a.Code })
            .ToDictionaryAsync(a => a.Id, a => $"{a.Name} – {a.Code}", ct);
}
