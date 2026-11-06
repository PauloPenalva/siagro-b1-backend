using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public class MarketInstrumentService(
    AppDbContext context, LiveQuoteStore store, ICompanyContext companyContext, ILogger<MarketInstrumentService> logger)
    : BaseService<MarketInstrument, string>(context, logger)
{
    private const decimal SoyTonFactor = 0.36745m;
    private const decimal CornTonFactor = 0.393678m;
    private const decimal BagTonFactor = 1000m / 60m;

    private static readonly char[] ForbiddenCodeChars = ['/', '\\', '\'', '%', '?', '#'];

    // Toda escrita bem-sucedida no cadastro avisa os terminais abertos (SSE): eles releem a lista/abas na hora.
    public override async Task<MarketInstrument> CreateAsync(MarketInstrument entity)
    {
        Validate(entity);
        var created = await MarketPersistence.AddAndSaveAsync(context, entity);
        NotifyTerminals();
        return created;
    }

    public override async Task<MarketInstrument?> UpdateAsync(string key, MarketInstrument entity)
    {
        Validate(entity);
        if ((!entity.Active || entity.QuoteUnit != MarketQuoteUnit.BrlPerUsd) && await IsSpotFxAsync(key))
            throw new BusinessException(
                "Este instrumento é o dólar à vista dos parâmetros do Mercado; troque-o nos parâmetros antes de inativá-lo ou mudar a unidade.");
        var updated = await base.UpdateAsync(key, entity);
        if (updated is not null)
            NotifyTerminals();
        return updated;
    }

    private async Task<bool> IsSpotFxAsync(string code)
    {
        var spot = await context.MarketTradingSettings.AsNoTracking().Select(s => s.SpotFxInstrumentCode).FirstOrDefaultAsync();
        return spot is not null && string.Equals(spot.Trim(), code?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyTerminals() => store.NotifyChanged(companyContext.CompanyCode);

    /// <summary>Com cotação ou fechamento o instrumento tem histórico: inativa-se, não se exclui.</summary>
    public override async Task<bool> DeleteAsync(string code)
    {
        if (await IsSpotFxAsync(code))
            throw new BusinessException(
                "Este instrumento é o dólar à vista dos parâmetros do Mercado; troque-o nos parâmetros antes de excluí-lo.");

        if (await context.MarketQuotes.AnyAsync(q => q.InstrumentCode == code)
            || await context.MarketDailyCloses.AnyAsync(c => c.InstrumentCode == code))
            throw new BusinessException("Instrumento possui cotações ou fechamentos; inative-o em vez de excluir.");

        if (await context.MarketPriceTableLines.AnyAsync(l => l.InstrumentCode == code || l.FxCurveInstrumentCode == code))
            throw new BusinessException("Instrumento é usado em linhas de tabelas de preços; inative-o em vez de excluir.");

        var deleted = await base.DeleteAsync(code);
        if (deleted)
            NotifyTerminals();
        return deleted;
    }

    // O código vai na URL do OData (chave): caracteres que quebram a rota ficam de fora.
    private static void Validate(MarketInstrument entity)
    {
        entity.Code = entity.Code?.Trim() ?? string.Empty;
        if (entity.Code.Length == 0)
            throw new BusinessException("Informe o código do instrumento.");
        if (entity.Code.IndexOfAny(ForbiddenCodeChars) >= 0)
            throw new BusinessException("O código do instrumento não pode conter / \\ ' % ? #.");
        if (entity.TonFactor <= 0)
            throw new BusinessException("O fator de conversão para tonelada deve ser maior que zero.");
        if (entity.ContractSize <= 0)
            throw new BusinessException("O tamanho do contrato deve ser maior que zero.");
    }

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
        NotifyTerminals();
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
        // SortOrder em blocos por grupo (10, 20, ...): a ordem padrão das abas é a do operador.
        var order = 10;

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

        order = 20;

        foreach (var (code, label) in new[] { ("ZCH7", "Mar/27"), ("ZCK7", "Mai/27"), ("ZCN7", "Jul/27") })
            Future(code, "Milho CBOT", "CORN", $"Milho CBOT {label}", MarketExchange.Cbot,
                MarketQuoteUnit.UsCentsPerBushel, CornTonFactor, 5000m);

        order = 30;

        foreach (var (code, label) in new[] { ("CCMF27", "Jan/27"), ("CCMH27", "Mar/27") })
            Future(code, "Milho B3", "CORN", $"Milho B3 {label}", MarketExchange.B3,
                MarketQuoteUnit.BrlPerBag, BagTonFactor, 450m);

        order = 40;

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
            SortOrder = order,
            Active = true,
        });

        order = 50;

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
