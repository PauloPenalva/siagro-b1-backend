using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public class MarketInstrumentService(AppDbContext context, ILogger<MarketInstrumentService> logger)
    : BaseService<MarketInstrument, string>(context, logger)
{
    private const decimal SoyTonFactor = 0.36745m;
    private const decimal CornTonFactor = 0.393678m;
    private const decimal BagTonFactor = 1000m / 60m;

    /// <summary>Cria, se ainda não existirem, os instrumentos de exemplo. Devolve quantos foram criados.</summary>
    public async Task<int> CreateSamplesAsync(CancellationToken cancellationToken)
    {
        var samples = BuildSamples();
        var existing = await context.MarketInstruments
            .Select(i => i.Code)
            .ToListAsync(cancellationToken);

        var missing = samples.Where(s => !existing.Contains(s.Code)).ToList();
        if (missing.Count == 0)
            return 0;

        context.MarketInstruments.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
        return missing.Count;
    }

    // Origens da CMA usadas pelo Siagro Tagui (CodigoBolsaService.getSymbolsActive / SourceIdEnum).
    private const int CmaSourceCbot = 30;
    private const int CmaSourceB3 = 57;
    private const int CmaSourceDollarSpot = 200;
    private const int CmaSourceDollarCurve = 87;

    private static List<MarketInstrument> BuildSamples()
    {
        var list = new List<MarketInstrument>();
        var order = 0;

        void Future(string code, string group, string commodity, string label, MarketExchange exchange,
            MarketQuoteUnit unit, decimal tonFactor, decimal contractSize) =>
            list.Add(new MarketInstrument
            {
                Code = code,
                Description = label,
                Exchange = exchange,
                // sourceId da CMA conforme o Siagro Tagui (SourceIdEnum): CBOT 30, B3/BM&F 57.
                CmaSourceId = exchange == MarketExchange.Cbot ? CmaSourceCbot : CmaSourceB3,
                Type = MarketInstrumentType.Future,
                Commodity = commodity,
                QuoteUnit = unit,
                ContractSize = contractSize,
                TonFactor = tonFactor,
                GroupName = group,
                SortOrder = order++,
                Active = true,
            });

        foreach (var (code, label) in new[]
                 {
                     ("ZSF7", "Jan/27"), ("ZSH7", "Mar/27"), ("ZSK7", "Mai/27"), ("ZSN7", "Jul/27"), ("ZSX6", "Nov/26"),
                 })
            Future(code, "Soja CBOT", "SOY", $"Soja CBOT {label}", MarketExchange.Cbot,
                MarketQuoteUnit.UsCentsPerBushel, SoyTonFactor, 5000m);

        foreach (var (code, label) in new[] { ("ZCH7", "Mar/27"), ("ZCK7", "Mai/27"), ("ZCN7", "Jul/27") })
            Future(code, "Milho CBOT", "CORN", $"Milho CBOT {label}", MarketExchange.Cbot,
                MarketQuoteUnit.UsCentsPerBushel, CornTonFactor, 5000m);

        foreach (var (code, label) in new[] { ("CCMF27", "Jan/27"), ("CCMH27", "Mar/27") })
            Future(code, "Milho B3", "CORN", $"Milho B3 {label}", MarketExchange.B3,
                MarketQuoteUnit.BrlPerBag, BagTonFactor, 450m);

        list.Add(new MarketInstrument
        {
            Code = "DOL COM",
            Description = "Dólar comercial",
            Exchange = MarketExchange.Spot,
            CmaSourceId = CmaSourceDollarSpot,
            Type = MarketInstrumentType.Spot,
            Commodity = "USD",
            QuoteUnit = MarketQuoteUnit.BrlPerUsd,
            ContractSize = 1m,
            TonFactor = 1m,
            GroupName = "Dólar comercial",
            SortOrder = order++,
            Active = true,
        });

        foreach (var days in new[] { 30, 60, 90, 180, 360 })
            list.Add(new MarketInstrument
            {
                Code = $"CURVA DE DOLAR {days}D",
                Description = $"Curva de dólar {days} dias",
                Exchange = MarketExchange.Spot,
                CmaSourceId = CmaSourceDollarCurve,
                Type = MarketInstrumentType.CurvePoint,
                Commodity = "USD",
                QuoteUnit = MarketQuoteUnit.BrlPerUsd,
                ContractSize = 1m,
                TonFactor = 1m,
                CurveDays = days,
                GroupName = "Curva de dólar",
                SortOrder = order++,
                Active = true,
            });

        return list;
    }
}
