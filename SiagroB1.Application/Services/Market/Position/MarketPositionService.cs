using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

public sealed record MarketCommodityBucket(int? Year, int? Month, string? InstrumentCode, decimal PurchaseTons, decimal SalesTons, decimal FuturesTons,
    decimal NetTons, decimal CoveredTons, decimal? HedgePercent, decimal? LastPrice, decimal? PreviousClose,
    decimal? FuturesUnrealized, decimal? FuturesUnrealizedAtClose, decimal? PhysicalExchangeMtm, int PhysicalWithoutReference, CurrencyType? Currency,
    string LimitState, decimal? MaxLong, decimal? MaxShort);

public sealed record MarketCommodityPosition(string Code, string Name, IReadOnlyList<MarketCommodityBucket> Buckets, MarketCommodityBucket Total);

public sealed record MarketUsdBucket(int? Year, int? Month, decimal ReceivableUsd, decimal PayableUsd, decimal FxHedgeUsd, decimal UsdFuturesUsd,
    decimal NetUsd, decimal CoveredUsd, decimal? HedgePercent, decimal? FxHedgeUnrealizedBrl, string LimitState, decimal? MaxLong, decimal? MaxShort);

public sealed record MarketUsdPosition(IReadOnlyList<MarketUsdBucket> Buckets, MarketUsdBucket Total, decimal? SpotFx, decimal OpenCommodityValueUsd);

/// <summary>Não realizado nulo quando há posição aberta naquela moeda que não pôde ser marcada; zero só sem posição.</summary>
public sealed record MarketResultSummary(decimal RealizedUsd, decimal RealizedBrl, decimal? UnrealizedUsd, decimal? UnrealizedBrl);

/// <summary>Limite estourado. <c>Scope</c>: commodity (t) | usd (US$); <c>LimitValue</c> sempre positivo; descrição pt-BR sem números.</summary>
public sealed record MarketLimitAlert(int LimitId, string Scope, string Description, decimal Value, decimal LimitValue, string State);

public sealed record MarketPositionSnapshot(DateTime GeneratedAt, IReadOnlyList<MarketCommodityPosition> Commodities, MarketUsdPosition Usd,
    MarketResultSummary Results, IReadOnlyList<MarketLimitAlert> Alerts, IReadOnlyList<MarketIgnoredContract> Ignored);

public sealed record MarketPositionContractRow(MarketEfpContractSide Side, Guid ContractKey, string ContractCode, string CardName, string ItemCode,
    DateOnly DeliveryEndDate, decimal FirmOpenTons, decimal PricePerBag, CurrencyType Currency, string? MarketInstrumentCode,
    decimal? MarketExchangePrice, decimal? MarketPremium, bool UsdLinked, bool FxLocked);

public sealed record MarketUsdContractRow(MarketEfpContractSide Side, Guid ContractKey, string ContractCode, string CardName, string ItemCode,
    DateOnly CashFlowDate, decimal FirmOpenTons, decimal? UsdAmount, decimal? UsdPricePerBag, bool FxLocked, decimal? FxRate);

/// <summary>
/// Painel de posição: exposição por commodity × mês de bolsa e em dólar, cobertura (hedge), marcação a mercado,
/// resultados e limites. Sempre calculado: físico do cache de 30 s (<see cref="MarketPhysicalExposureService"/>),
/// futuros da parte 3, NDF/termo, cotações e fechamentos. Convenções de sinal: compra/comprado/a receber (+),
/// venda/vendido/a pagar (−); <c>PurchaseTons</c>, <c>SalesTons</c>, <c>ReceivableUsd</c> e <c>PayableUsd</c> são magnitudes.
/// </summary>
public class MarketPositionService(
    AppDbContext context, MarketPhysicalExposureService physical, MarketDerivativesPositionService derivatives, TimeProvider time)
{
    public const decimal KgPerBag = 60m;

    private sealed record InstrumentData(string Code, string Commodity, MarketInstrumentType Type, bool Active, DateTime? ExpirationDate,
        MarketQuoteUnit QuoteUnit, decimal ContractSize, decimal TonFactor);

    private sealed record LimitData(int Id, MarketLimitScope Scope, string? CommodityCode, int? Year, int? Month, decimal? MaxLong, decimal? MaxShort);

    private sealed record Evaluated(string State, decimal? MaxLong, decimal? MaxShort, MarketLimitAlert? Alert);

    /// <param name="bypassCache">true lê o físico direto do banco, sem o cache de 30 s (alerta de limite).</param>
    public virtual async Task<MarketPositionSnapshot> GetSnapshotAsync(DateOnly today, CancellationToken ct, bool bypassCache = false)
    {
        var exposure = bypassCache ? await physical.ReadAsync(ct) : await physical.GetAsync(ct);
        var futures = await derivatives.GetOpenPositionsAsync(ct);

        var spotCode = (await context.MarketTradingSettings.AsNoTracking()
            .OrderBy(s => s.Key).Select(s => s.SpotFxInstrumentCode).FirstOrDefaultAsync(ct))?.Trim();
        if (string.IsNullOrEmpty(spotCode)) spotCode = null;

        var names = await context.MarketCommodities.AsNoTracking()
            .Select(c => new { c.Code, c.Name }).ToListAsync(ct);

        var referenced = exposure.Rows.Where(r => r.MarketInstrumentCode != null)
            .Select(r => r.MarketInstrumentCode!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var futureCodes = futures.Select(f => f.InstrumentCode).ToList();
        var extra = referenced.Concat(futureCodes).Concat(spotCode is null ? [] : [spotCode]).ToList();
        // Vencidos há mais de um mês só entram se tiverem posição aberta (estão em "extra").
        var recentExpiration = today.AddMonths(-1).ToDateTime(TimeOnly.MinValue);
        var instruments = await context.MarketInstruments.AsNoTracking()
            .Where(i => (i.Active && i.Type == MarketInstrumentType.Future && i.ExpirationDate != null && i.ExpirationDate >= recentExpiration)
                        || extra.Contains(i.Code))
            .Select(i => new InstrumentData(i.Code, i.Commodity, i.Type, i.Active, i.ExpirationDate, i.QuoteUnit, i.ContractSize, i.TonFactor))
            .ToListAsync(ct);
        var instrumentByCode = instruments
            .GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var codes = instrumentByCode.Keys.ToList();

        var lastByCode = (await context.MarketQuotes.AsNoTracking()
                .Where(q => codes.Contains(q.InstrumentCode))
                .Select(q => new { q.InstrumentCode, q.Last }).ToListAsync(ct))
            .Where(q => q.Last.HasValue)
            .GroupBy(q => q.InstrumentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Last!.Value, StringComparer.OrdinalIgnoreCase);

        // Último fechamento diário (até hoje) de cada instrumento: subconsulta correlacionada, uma ida ao banco.
        var todayDate = today.ToDateTime(TimeOnly.MinValue);
        var closeByCode = (await context.MarketDailyCloses.AsNoTracking()
                .Where(c => codes.Contains(c.InstrumentCode) && c.Date <= todayDate
                            && c.Date == context.MarketDailyCloses
                                .Where(d => d.InstrumentCode == c.InstrumentCode && d.Date <= todayDate).Max(d => d.Date))
                .Select(c => new { c.InstrumentCode, c.Close }).ToListAsync(ct))
            .GroupBy(c => c.InstrumentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Close, StringComparer.OrdinalIgnoreCase);

        var limits = await context.MarketExposureLimits.AsNoTracking().Where(l => l.Active)
            .OrderBy(l => l.Id)
            .Select(l => new LimitData(l.Id, l.Scope, l.CommodityCode, l.Year, l.Month, l.MaxLong, l.MaxShort))
            .ToListAsync(ct);

        var yearStart = new DateTime(today.Year, 1, 1);
        var yearEnd = new DateTime(today.Year, 12, 31);
        var fxHedges = await context.MarketFxHedges.AsNoTracking()
            .Where(h => h.Status == MarketFxHedgeStatus.Open
                        || (h.Status == MarketFxHedgeStatus.Settled && h.SettlementDate >= yearStart && h.SettlementDate <= yearEnd))
            .Select(h => new { h.Status, h.Side, h.AmountUsd, h.ContractedRate, h.SettlementDate, h.RealizedPnlBrl })
            .ToListAsync(ct);

        var realized = await context.MarketFuturesMatches.AsNoTracking()
            .Where(m => m.CloseDate >= yearStart && m.CloseDate <= yearEnd)
            .GroupBy(m => m.Currency)
            .Select(g => new { Currency = g.Key, Total = g.Sum(m => m.RealizedPnl) })
            .ToListAsync(ct);

        decimal? spotFx = null;
        if (spotCode is not null && instrumentByCode.TryGetValue(spotCode, out var spot)
            && spot is { Active: true, QuoteUnit: MarketQuoteUnit.BrlPerUsd } && lastByCode.TryGetValue(spot.Code, out var spotLast))
            spotFx = spotLast;

        var alerts = new List<MarketLimitAlert>();
        var commodities = BuildCommodities(exposure, futures, names.ToDictionary(n => n.Code, n => n.Name, StringComparer.OrdinalIgnoreCase),
            instruments, instrumentByCode, lastByCode, closeByCode, limits, alerts, out var openCommodityValueUsd);

        var usd = BuildUsd(exposure, futures,
            fxHedges.Where(h => h.Status == MarketFxHedgeStatus.Open)
                .Select(h => (h.Side, h.AmountUsd, h.ContractedRate, Settlement: DateOnly.FromDateTime(h.SettlementDate))).ToList(),
            spotFx, openCommodityValueUsd, limits, alerts);

        var openNdfs = fxHedges.Where(h => h.Status == MarketFxHedgeStatus.Open).ToList();
        decimal? openNdfBrl = openNdfs.Count == 0 ? 0m
            : spotFx is { } fx ? openNdfs.Sum(h => MarketFxMath.RealizedPnlBrl(h.Side, h.ContractedRate, fx, h.AmountUsd))
            : null;
        var results = new MarketResultSummary(
            realized.Where(r => r.Currency == CurrencyType.Usd).Sum(r => r.Total),
            realized.Where(r => r.Currency == CurrencyType.Brl).Sum(r => r.Total)
                + fxHedges.Where(h => h.Status == MarketFxHedgeStatus.Settled).Sum(h => h.RealizedPnlBrl ?? 0m),
            Unrealized(futures, CurrencyType.Usd),
            Unrealized(futures, CurrencyType.Brl) + openNdfBrl);

        return new MarketPositionSnapshot(time.GetUtcNow().UtcDateTime, commodities, usd, results, alerts, exposure.Ignored);
    }

    /// <summary>Linhas físicas que formam um balde (commodity × mês de bolsa).</summary>
    public async Task<IReadOnlyList<MarketPositionContractRow>> GetBucketContractsAsync(string commodity, int? year, int? month, CancellationToken ct)
    {
        var exposure = await physical.GetAsync(ct);
        return exposure.Rows
            .Where(r => string.Equals(r.CommodityCode, commodity.Trim(), StringComparison.OrdinalIgnoreCase)
                        // Sem ano e mês = linha de total da commodity: todos os vencimentos.
                        && (year is null || (r.FuturesYear == year && r.FuturesMonth == month)))
            .OrderBy(r => r.Side).ThenBy(r => r.FuturesYear).ThenBy(r => r.FuturesMonth).ThenBy(r => r.ContractCode)
            .Select(r => new MarketPositionContractRow(r.Side, r.ContractKey, r.ContractCode, r.CardName, r.ItemCode, r.DeliveryEndDate,
                r.FirmOpenKg / 1000m, Math.Round(r.PricePerKg * KgPerBag, 2, MidpointRounding.AwayFromZero), r.Currency,
                r.MarketInstrumentCode, r.MarketExchangePrice, r.MarketPremium, r.UsdLinked, r.FxLocked))
            .ToList();
    }

    /// <summary>
    /// Todos os contratos atrelados a dólar com saldo firme a entregar, travados ou não (os travados para reabrir),
    /// independente de commodity e de regra de mês (lista <see cref="MarketPhysicalExposure.UsdRows"/>).
    /// </summary>
    public async Task<IReadOnlyList<MarketUsdContractRow>> GetUsdContractsAsync(CancellationToken ct) =>
        (await physical.GetAsync(ct)).UsdRows
            .OrderBy(r => r.CashFlowDate).ThenBy(r => r.ContractCode)
            .Select(r => new MarketUsdContractRow(r.Side, r.ContractKey, r.ContractCode, r.CardName, r.ItemCode, r.CashFlowDate,
                r.FirmOpenKg / 1000m, UsdAmount(r), Math.Round(r.UsdPricePerKg * KgPerBag, 4, MidpointRounding.AwayFromZero),
                r.FxLocked, r.MarketFxRate))
            .ToList();

    private static decimal UsdAmount(MarketUsdRow r) => Math.Round(r.FirmOpenKg * r.UsdPricePerKg, 2, MidpointRounding.AwayFromZero);

    /// <summary>Não realizado dos futuros abertos de uma moeda; nulo se algum não tem cotação, zero se não há posição.</summary>
    private static decimal? Unrealized(IReadOnlyList<MarketFuturesOpenPosition> futures, CurrencyType currency)
    {
        var open = futures.Where(f => f.Currency == currency).ToList();
        return open.Any(f => f.UnrealizedPnl is null) ? null : open.Sum(f => f.UnrealizedPnl!.Value);
    }

    /// <summary>
    /// Valor, na moeda da cotação, por tonelada de 1 unidade de preço: US¢/bu → TonFactor (US$/t); US$/t → 1 (US$/t);
    /// R$/sc → TonFactor (R$/t). Nulo sem tonelagem.
    /// </summary>
    private static decimal? ValuePerTon(InstrumentData i) => i.QuoteUnit switch
    {
        MarketQuoteUnit.UsCentsPerBushel or MarketQuoteUnit.BrlPerBag => i.TonFactor > 0 ? i.TonFactor : null,
        MarketQuoteUnit.UsdPerTon => 1m,
        _ => null,
    };

    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private List<MarketCommodityPosition> BuildCommodities(
        MarketPhysicalExposure exposure, IReadOnlyList<MarketFuturesOpenPosition> futures, Dictionary<string, string> names,
        List<InstrumentData> instruments, Dictionary<string, InstrumentData> instrumentByCode,
        Dictionary<string, decimal> lastByCode, Dictionary<string, decimal> closeByCode, List<LimitData> limits,
        List<MarketLimitAlert> alerts, out decimal openCommodityValueUsd)
    {
        openCommodityValueUsd = 0m;
        var commodityFutures = futures.Where(f => f.Tons.HasValue).ToList();
        var keys = exposure.Rows.Select(r => (Commodity: r.CommodityCode.ToUpperInvariant(), Year: (int?)r.FuturesYear, Month: (int?)r.FuturesMonth))
            .Concat(commodityFutures.Select(f => (Commodity: f.Commodity.ToUpperInvariant(), Year: f.ExpirationDate?.Year, Month: f.ExpirationDate?.Month)))
            .Distinct().ToList();

        var result = new List<MarketCommodityPosition>();
        foreach (var group in keys.GroupBy(k => k.Commodity).OrderBy(g => g.Key))
        {
            var code = group.Key;
            var name = names.GetValueOrDefault(code) ?? code;
            var buckets = new List<MarketCommodityBucket>();
            foreach (var k in group.OrderBy(k => k.Year is null).ThenBy(k => k.Year).ThenBy(k => k.Month))
            {
                var rows = exposure.Rows.Where(r => r.CommodityCode.Equals(code, StringComparison.OrdinalIgnoreCase)
                                                    && r.FuturesYear == k.Year && r.FuturesMonth == k.Month).ToList();
                var positions = commodityFutures.Where(f => f.Commodity.Equals(code, StringComparison.OrdinalIgnoreCase)
                                                            && f.ExpirationDate?.Year == k.Year && f.ExpirationDate?.Month == k.Month).ToList();
                var bucket = BuildBucket(code, k.Year, k.Month, rows, positions, instruments, instrumentByCode, lastByCode, closeByCode);
                var eval = Evaluate(limits, MarketLimitScope.Commodity, code, k.Year, k.Month, bucket.NetTons, Describe(name, k.Year, k.Month));
                bucket = bucket with { LimitState = eval.State, MaxLong = eval.MaxLong, MaxShort = eval.MaxShort };
                if (eval.Alert is not null) alerts.Add(eval.Alert);

                if (bucket.Currency == CurrencyType.Usd && bucket.LastPrice is { } last && bucket.InstrumentCode is { } rep
                    && ValuePerTon(instrumentByCode[rep]) is { } perTon)
                    openCommodityValueUsd += Round2(bucket.NetTons * last * perTon);
                buckets.Add(bucket);
            }

            var referencedRows = exposure.Rows.Where(r => r.CommodityCode.Equals(code, StringComparison.OrdinalIgnoreCase)
                                                         && !string.IsNullOrWhiteSpace(r.MarketInstrumentCode) && r.MarketExchangePrice.HasValue)
                .Select(r => (int?)r.FuturesYear * 100 + r.FuturesMonth).ToHashSet();
            var total = Total(buckets, b => referencedRows.Contains(b.Year * 100 + b.Month));
            var totalEval = Evaluate(limits, MarketLimitScope.Commodity, code, null, null, total.NetTons, Describe(name, null, null), isTotal: true);
            total = total with { LimitState = totalEval.State, MaxLong = totalEval.MaxLong, MaxShort = totalEval.MaxShort };
            if (totalEval.Alert is not null) alerts.Add(totalEval.Alert);
            result.Add(new MarketCommodityPosition(code, name, buckets, total));
        }

        return result;
    }

    private static MarketCommodityBucket BuildBucket(string commodity, int? year, int? month, List<MarketPhysicalRow> rows,
        List<MarketFuturesOpenPosition> positions, List<InstrumentData> instruments, Dictionary<string, InstrumentData> instrumentByCode,
        Dictionary<string, decimal> lastByCode, Dictionary<string, decimal> closeByCode)
    {
        var purchaseTons = rows.Where(r => r.Side == MarketEfpContractSide.Purchase).Sum(r => r.FirmOpenKg) / 1000m;
        var salesTons = rows.Where(r => r.Side == MarketEfpContractSide.Sales).Sum(r => r.FirmOpenKg) / 1000m;
        var futuresTons = positions.Sum(p => p.Tons!.Value);
        var physicalNet = purchaseTons - salesTons;

        // Instrumento que representa o balde: futuro ativo da commodity que vence no mês; com posição aberta primeiro.
        InstrumentData? rep = null;
        if (year.HasValue)
        {
            var candidates = instruments.Where(i => i is { Active: true, Type: MarketInstrumentType.Future }
                                                    && i.Commodity.Equals(commodity, StringComparison.OrdinalIgnoreCase)
                                                    && i.ExpirationDate?.Year == year && i.ExpirationDate?.Month == month)
                .OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ToList();
            rep = candidates.FirstOrDefault(i => positions.Any(p => p.InstrumentCode.Equals(i.Code, StringComparison.OrdinalIgnoreCase)))
                  ?? candidates.FirstOrDefault();
        }
        else if (positions.Count > 0)
        {
            instrumentByCode.TryGetValue(positions.OrderBy(p => p.InstrumentCode).First().InstrumentCode, out rep);
        }

        var currency = rep is not null ? MarketUnits.QuoteCurrency(rep.QuoteUnit)
            : positions.Select(p => p.Currency).Distinct().Count() == 1 ? positions[0].Currency : (CurrencyType?)null;
        decimal? last = rep is not null && lastByCode.TryGetValue(rep.Code, out var l) ? l : null;
        decimal? close = rep is not null && closeByCode.TryGetValue(rep.Code, out var c) ? c : null;

        // Derivativos: soma só quando há posição, todas marcadas e na moeda do balde.
        decimal? futuresUnrealized = null, futuresAtClose = null;
        if (positions.Count > 0 && currency.HasValue && positions.All(p => p.Currency == currency))
        {
            if (positions.All(p => p.UnrealizedPnl.HasValue))
                futuresUnrealized = positions.Sum(p => p.UnrealizedPnl!.Value);
            if (positions.All(p => closeByCode.ContainsKey(p.InstrumentCode)))
                futuresAtClose = positions.Sum(p =>
                {
                    var i = instrumentByCode[p.InstrumentCode];
                    return Round2((closeByCode[p.InstrumentCode] - p.AvgPrice) * p.NetLots * MarketUnits.PointValue(i.QuoteUnit, i.ContractSize));
                });
        }

        // Físico: só o componente de bolsa dos contratos com referência; os demais são contados.
        var withReference = rows.Where(r => !string.IsNullOrWhiteSpace(r.MarketInstrumentCode) && r.MarketExchangePrice.HasValue).ToList();
        decimal? physicalMtm = null;
        if (withReference.Count > 0)
        {
            decimal sum = 0m;
            var complete = true;
            foreach (var r in withReference)
            {
                if (!instrumentByCode.TryGetValue(r.MarketInstrumentCode!.Trim(), out var i) || ValuePerTon(i) is not { } perTon
                    || !lastByCode.TryGetValue(i.Code, out var quote) || (currency.HasValue && MarketUnits.QuoteCurrency(i.QuoteUnit) != currency))
                {
                    complete = false;
                    break;
                }
                var sign = r.Side == MarketEfpContractSide.Purchase ? 1m : -1m;
                sum += (quote - r.MarketExchangePrice!.Value) * perTon * (r.FirmOpenKg / 1000m) * sign;
            }
            physicalMtm = complete ? Round2(sum) : null;
        }

        return new MarketCommodityBucket(year, month, rep?.Code, purchaseTons, salesTons, futuresTons, physicalNet + futuresTons,
            MarketPositionMath.Covered(physicalNet, futuresTons), MarketPositionMath.HedgePercent(physicalNet, futuresTons),
            last, close, futuresUnrealized, futuresAtClose, physicalMtm, rows.Count - withReference.Count, currency, "none", null, null);
    }

    /// <summary>Total da commodity: soma dos baldes; valores em dinheiro só quando todos os baldes têm a mesma moeda.</summary>
    private static MarketCommodityBucket Total(List<MarketCommodityBucket> buckets, Func<MarketCommodityBucket, bool> hasReferenced)
    {
        var purchase = buckets.Sum(b => b.PurchaseTons);
        var sales = buckets.Sum(b => b.SalesTons);
        var fut = buckets.Sum(b => b.FuturesTons);
        var currencies = buckets.Where(b => b.Currency.HasValue).Select(b => b.Currency!.Value).Distinct().ToList();
        var currency = currencies.Count == 1 ? currencies[0] : (CurrencyType?)null;
        decimal? Money(Func<MarketCommodityBucket, decimal?> pick, Func<MarketCommodityBucket, bool> applicable)
        {
            var relevant = buckets.Where(applicable).ToList();
            if (currency is null || relevant.Count == 0 || relevant.Any(b => pick(b) is null)) return null;
            return relevant.Sum(b => pick(b)!.Value);
        }

        return new MarketCommodityBucket(null, null, null, purchase, sales, fut, purchase - sales + fut,
            MarketPositionMath.Covered(purchase - sales, fut), MarketPositionMath.HedgePercent(purchase - sales, fut),
            null, null,
            Money(b => b.FuturesUnrealized, b => b.FuturesTons != 0m),
            Money(b => b.FuturesUnrealizedAtClose, b => b.FuturesTons != 0m),
            Money(b => b.PhysicalExchangeMtm, hasReferenced),
            buckets.Sum(b => b.PhysicalWithoutReference), currency, "none", null, null);
    }

    private MarketUsdPosition BuildUsd(MarketPhysicalExposure exposure, IReadOnlyList<MarketFuturesOpenPosition> futures,
        List<(MarketTradeSide Side, decimal AmountUsd, decimal ContractedRate, DateOnly Settlement)> openHedges,
        decimal? spotFx, decimal openCommodityValueUsd, List<LimitData> limits, List<MarketLimitAlert> alerts)
    {
        var contracts = exposure.UsdRows.Where(r => !r.FxLocked).ToList();
        var dollarFutures = futures.Where(f => f.Usd.HasValue).ToList();
        var keys = contracts.Select(r => ((int?)r.CashFlowDate.Year, (int?)r.CashFlowDate.Month))
            .Concat(openHedges.Select(h => ((int?)h.Settlement.Year, (int?)h.Settlement.Month)))
            .Concat(dollarFutures.Select(f => (f.ExpirationDate?.Year, f.ExpirationDate?.Month)))
            .Distinct().OrderBy(k => k.Item1 is null).ThenBy(k => k.Item1).ThenBy(k => k.Item2).ToList();

        var buckets = new List<MarketUsdBucket>();
        foreach (var (year, month) in keys)
        {
            var rows = contracts.Where(r => r.CashFlowDate.Year == year && r.CashFlowDate.Month == month).ToList();
            var hedges = openHedges.Where(h => h.Settlement.Year == year && h.Settlement.Month == month).ToList();
            var receivable = rows.Where(r => r.Side == MarketEfpContractSide.Sales).Sum(UsdAmount);
            var payable = rows.Where(r => r.Side == MarketEfpContractSide.Purchase).Sum(UsdAmount);
            var fxHedge = hedges.Sum(h => h.Side == MarketTradeSide.Sell ? -h.AmountUsd : h.AmountUsd);
            var usdFutures = dollarFutures.Where(f => f.ExpirationDate?.Year == year && f.ExpirationDate?.Month == month).Sum(f => f.Usd!.Value);
            decimal? unrealized = hedges.Count > 0 && spotFx is { } fx
                ? hedges.Sum(h => MarketFxMath.RealizedPnlBrl(h.Side, h.ContractedRate, fx, h.AmountUsd))
                : null;
            buckets.Add(UsdBucket(year, month, receivable, payable, fxHedge, usdFutures, unrealized, limits, alerts));
        }

        var hasHedges = openHedges.Count > 0;
        decimal? totalUnrealized = hasHedges && spotFx.HasValue ? buckets.Sum(b => b.FxHedgeUnrealizedBrl ?? 0m) : null;
        var total = UsdBucket(null, null, buckets.Sum(b => b.ReceivableUsd), buckets.Sum(b => b.PayableUsd), buckets.Sum(b => b.FxHedgeUsd),
            buckets.Sum(b => b.UsdFuturesUsd), totalUnrealized, limits, alerts, isTotal: true);
        return new MarketUsdPosition(buckets, total, spotFx, openCommodityValueUsd);
    }

    private static MarketUsdBucket UsdBucket(int? year, int? month, decimal receivable, decimal payable, decimal fxHedge, decimal usdFutures,
        decimal? unrealized, List<LimitData> limits, List<MarketLimitAlert> alerts, bool isTotal = false)
    {
        var physicalNet = receivable - payable;
        var hedge = fxHedge + usdFutures;
        var net = physicalNet + hedge;
        var eval = Evaluate(limits, MarketLimitScope.Usd, null, year, month, net, Describe("Dólar", year, month), isTotal);
        if (eval.Alert is not null) alerts.Add(eval.Alert);
        return new MarketUsdBucket(year, month, receivable, payable, fxHedge, usdFutures, net,
            MarketPositionMath.Covered(physicalNet, hedge), MarketPositionMath.HedgePercent(physicalNet, hedge), unrealized,
            eval.State, eval.MaxLong, eval.MaxShort);
    }

    private static string Describe(string name, int? year, int? month) =>
        year.HasValue ? $"{name} — {month:00}/{year}" : $"{name} — total";

    /// <summary>
    /// Limite ativo do balde: ano+mês → só aquele balde; ambos nulos → o total. Baldes "sem vencimento" não têm limite próprio.
    /// </summary>
    private static Evaluated Evaluate(List<LimitData> limits, MarketLimitScope scope, string? commodity, int? year, int? month,
        decimal net, string description, bool isTotal = false)
    {
        if (!isTotal && year is null)
            return new Evaluated("none", null, null, null);

        var limit = limits.FirstOrDefault(l => l.Scope == scope
                                               && (scope == MarketLimitScope.Usd
                                                   || string.Equals(l.CommodityCode, commodity, StringComparison.OrdinalIgnoreCase))
                                               && l.Year == year && l.Month == month);
        if (limit is null)
            return new Evaluated("none", null, null, null);

        var state = MarketPositionMath.Evaluate(net, limit.MaxLong, limit.MaxShort);
        var text = state switch
        {
            MarketPositionMath.LimitState.LongExceeded => "longExceeded",
            MarketPositionMath.LimitState.ShortExceeded => "shortExceeded",
            _ => "ok",
        };
        MarketLimitAlert? alert = state == MarketPositionMath.LimitState.Ok
            ? null
            : new MarketLimitAlert(limit.Id, scope == MarketLimitScope.Usd ? "usd" : "commodity", description, net,
                state == MarketPositionMath.LimitState.LongExceeded ? limit.MaxLong!.Value : limit.MaxShort!.Value, text);
        return new Evaluated(text, limit.MaxLong, limit.MaxShort, alert);
    }
}
