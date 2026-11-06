using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Pricing;

/// <summary>
/// Linha da grade "Ordem de Compra": preço de origem (R$/saca 60 kg) de uma linha da tabela de preços para a cidade,
/// com tudo que o originou (cotação usada, dólar, frete e passos) para ser rastreável. Datas ausentes saem nulas
/// (nunca 0001-01-01). <c>FxQuotedAt</c>/<c>FxQuoteSource</c> são do dólar MAIS ANTIGO entre o à vista e o da curva.
/// </summary>
public sealed record MarketPriceGridRow(
    int LineId, int PriceTableId, string PriceTableDescription, string ItemCode, string ItemName, string LineDescription,
    string InstrumentCode, int DestinationId, string DestinationName,
    DateOnly? DeliveryStartDate, DateOnly? DeliveryEndDate, DateOnly? ReceiptDate, DateOnly? PaymentDate,
    decimal? ExchangeQuote, decimal? ExchangeLast, decimal? ChangePercent, decimal Premium, decimal? SpotFx, decimal? NetFx,
    decimal? PortPrice, decimal? FreightPerTon, decimal? Price, MarketPriceStatus Status,
    DateTime? QuotedAt, MarketQuoteSource? QuoteSource, IReadOnlyList<MarketPriceStep> Steps,
    string HarvestSeasonCode, decimal? CurveFx = null, DateTime? FxQuotedAt = null, MarketQuoteSource? FxQuoteSource = null);

/// <summary>Grade + estado da conexão CMA da empresa (preenchido pelo controller, igual ao SSE do terminal).</summary>
public sealed record MarketPriceGrid(
    string HarvestSeasonCode, string MunicipalityCode, DateTime GeneratedAt, IReadOnlyList<MarketPriceGridRow> Rows)
{
    public CmaConnectionStatus? ConnectionStatus { get; init; }

    public string? ConnectionMessage { get; init; }
}

/// <summary>
/// Monta a grade de preços: lê linhas ativas (com instrumentos e destino), parâmetros, cotações e fretes vigentes
/// (4 consultas) e aplica <see cref="MarketPriceCalculator"/> linha a linha. Problema numa linha vira status, nunca exceção.
/// </summary>
public class MarketPriceGridService(AppDbContext context, MarketFreightRateService freightRates)
{
    public async Task<MarketPriceGrid> GetGridAsync(
        string harvestSeasonCode, string municipalityCode, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await BuildRowsAsync(
            l => l.PriceTable!.HarvestSeasonCode == harvestSeasonCode, municipalityCode, today, cancellationToken);
        return new MarketPriceGrid(harvestSeasonCode, municipalityCode, DateTime.UtcNow, rows);
    }

    /// <summary>A mesma linha que a grade mostraria (usada pela boleta); nulo se a linha não existe ou está inativa.</summary>
    public async Task<MarketPriceGridRow?> GetRowAsync(
        int priceTableLineId, string municipalityCode, DateOnly today, CancellationToken cancellationToken) =>
        (await BuildRowsAsync(l => l.Id == priceTableLineId, municipalityCode, today, cancellationToken)).FirstOrDefault();

    private sealed record LineData(
        int LineId, int PriceTableId, string PriceTableDescription, string ItemCode, string ItemName, string LineDescription,
        string InstrumentCode, string? FxCurveInstrumentCode, int DestinationId, string? DestinationName, bool? DestinationActive,
        decimal Premium, decimal ElevationCost, decimal FxDiscount, decimal DiscountValue, bool DiscountIsPercent,
        decimal MarginPercent, decimal DailyInterestPercent, decimal TechnicalLossPercent, decimal RuralTaxPercent,
        decimal FreightCoefficientPercent, DateTime? DeliveryStartDate, DateTime? DeliveryEndDate, DateTime? ReceiptDate,
        DateTime? PaymentDate, int SortOrder,
        bool? InstrumentActive, MarketQuoteUnit? QuoteUnit, decimal? TonFactor,
        bool? CurveActive, MarketInstrumentType? CurveType, int? CurveDays, string HarvestSeasonCode);

    private async Task<List<MarketPriceGridRow>> BuildRowsAsync(
        Expression<Func<MarketPriceTableLine, bool>> filter, string municipalityCode, DateOnly today, CancellationToken ct)
    {
        // Consulta 1: linhas ativas de tabelas ativas, com os dados de instrumento e destino.
        var lines = await context.MarketPriceTableLines.AsNoTracking()
            .Where(l => l.Active && l.PriceTable!.Active)
            .Where(filter)
            .Select(l => new LineData(
                l.Id, l.PriceTableId, l.PriceTable!.Description, l.PriceTable.ItemCode, l.PriceTable.ItemName!, l.Description,
                l.InstrumentCode, l.FxCurveInstrumentCode, l.DestinationId, l.Destination!.Name, (bool?)l.Destination.Active,
                l.Premium, l.ElevationCost, l.FxDiscount, l.DiscountValue, l.DiscountIsPercent,
                l.MarginPercent, l.DailyInterestPercent, l.TechnicalLossPercent, l.RuralTaxPercent,
                l.FreightCoefficientPercent, l.DeliveryStartDate, l.DeliveryEndDate, l.ReceiptDate,
                l.PaymentDate, l.SortOrder,
                (bool?)l.Instrument!.Active, (MarketQuoteUnit?)l.Instrument.QuoteUnit, (decimal?)l.Instrument.TonFactor,
                (bool?)l.FxCurveInstrument!.Active, (MarketInstrumentType?)l.FxCurveInstrument.Type, l.FxCurveInstrument.CurveDays,
                l.PriceTable.HarvestSeasonCode))
            .ToListAsync(ct);
        if (lines.Count == 0)
            return [];

        // Consulta 2: parâmetros gerais (instrumento do dólar spot).
        var spotCode = (await context.MarketTradingSettings.AsNoTracking()
            .Select(s => s.SpotFxInstrumentCode).FirstOrDefaultAsync(ct))?.Trim();
        if (string.IsNullOrEmpty(spotCode))
            spotCode = null;

        // Instrumento do dólar à vista: desativado, inexistente ou que não seja R$/US$ invalida as linhas em US$.
        var spotValid = true;
        if (spotCode is not null)
        {
            var spot = await context.MarketInstruments.AsNoTracking()
                .Where(i => i.Code == spotCode)
                .Select(i => new { i.Active, i.QuoteUnit })
                .FirstOrDefaultAsync(ct);
            spotValid = spot is { Active: true, QuoteUnit: MarketQuoteUnit.BrlPerUsd };
        }

        // Consulta 3: cotações de tudo que a grade usa.
        var codes = lines.Select(l => l.InstrumentCode)
            .Concat(lines.Where(l => l.FxCurveInstrumentCode != null).Select(l => l.FxCurveInstrumentCode!))
            .Concat(spotCode is null ? [] : [spotCode])
            .Distinct().ToList();
        var quotes = (await context.MarketQuotes.AsNoTracking()
                .Where(q => codes.Contains(q.InstrumentCode))
                .ToListAsync(ct))
            .ToDictionary(q => q.InstrumentCode, StringComparer.OrdinalIgnoreCase);

        // Consulta 4: frete vigente por destino.
        var freight = await freightRates.GetEffectiveRatesAsync(municipalityCode, today, ct);

        MarketQuote? Quote(string? code) => code is not null && quotes.TryGetValue(code, out var q) ? q : null;
        // Dólar: sempre o último negócio; nulo ou ≤ 0 não serve.
        MarketQuote? Fx(string? code) => Quote(code) is { Last: > 0 } q ? q : null;

        return lines
            .OrderBy(l => l.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.PriceTableId)
            .ThenBy(l => l.SortOrder)
            .ThenBy(l => l.PaymentDate)
            .Select(l => BuildRow(l, Quote(l.InstrumentCode), spotValid, Fx(spotCode), Fx(l.FxCurveInstrumentCode),
                freight.TryGetValue(l.DestinationId, out var rate) ? rate : null, today))
            .ToList();
    }

    private static MarketPriceGridRow BuildRow(
        LineData l, MarketQuote? quote, bool spotValid, MarketQuote? spotQuote, MarketQuote? curveQuote,
        decimal? freightPerTon, DateOnly today)
    {
        var isUsd = l.QuoteUnit is MarketQuoteUnit.UsCentsPerBushel or MarketQuoteUnit.UsdPerTon;
        var invalid = l.InstrumentActive != true
            || l.DestinationActive != true
            || l.ReceiptDate is null || l.PaymentDate is null
            || l.DeliveryStartDate is null || l.DeliveryEndDate is null
            || (isUsd && (!spotValid || l.CurveActive != true || l.CurveType != MarketInstrumentType.CurvePoint
                          || l.CurveDays is not > 0));

        var used = quote is null ? null : quote.Bid is > 0 ? quote.Bid : quote.Last;
        var receipt = ToDate(l.ReceiptDate);
        var payment = ToDate(l.PaymentDate);
        var spotFx = spotQuote?.Last;
        var curveFx = curveQuote?.Last;
        // Idade do dólar: vale o carimbo mais antigo entre o à vista e o da curva (só para linhas em US$).
        var oldestFx = isUsd
            ? new[] { spotQuote, curveQuote }.Where(q => q is not null).MinBy(q => q!.QuotedAt)
            : null;

        var result = invalid
            ? new MarketPriceResult(MarketPriceStatus.InvalidSetup, null, null, null, null, [])
            : MarketPriceCalculator.Calculate(new MarketPriceInputs(
                used, l.QuoteUnit!.Value, l.TonFactor!.Value, l.Premium, l.ElevationCost,
                spotFx, curveFx, l.CurveDays, l.FxDiscount,
                today, receipt!.Value, payment!.Value,
                l.DiscountValue, l.DiscountIsPercent, l.MarginPercent, l.DailyInterestPercent,
                l.TechnicalLossPercent, l.RuralTaxPercent, freightPerTon, l.FreightCoefficientPercent));

        return new MarketPriceGridRow(
            l.LineId, l.PriceTableId, l.PriceTableDescription, l.ItemCode, l.ItemName, l.LineDescription,
            l.InstrumentCode, l.DestinationId, l.DestinationName ?? string.Empty,
            ToDate(l.DeliveryStartDate), ToDate(l.DeliveryEndDate), receipt, payment,
            used, quote?.Last, quote?.ChangePercent, l.Premium, isUsd ? spotFx : null, result.NetFx,
            result.PortPrice, freightPerTon, result.Price, result.Status,
            quote is null ? null : DateTime.SpecifyKind(quote.QuotedAt, DateTimeKind.Utc), quote?.Source, result.Steps,
            l.HarvestSeasonCode,
            isUsd ? curveFx : null,
            oldestFx is null ? null : DateTime.SpecifyKind(oldestFx.QuotedAt, DateTimeKind.Utc),
            oldestFx?.Source);
    }

    private static DateOnly? ToDate(DateTime? value) => value is null ? null : DateOnly.FromDateTime(value.Value);
}
