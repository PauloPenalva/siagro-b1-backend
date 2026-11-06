using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>
/// Aritmética pura de unidades do Mercado: moeda da cotação, valor do ponto, toneladas por lote
/// e resultado realizado de um casamento. Sem dependências e sem "if" por commodity.
/// </summary>
public static class MarketUnits
{
    public static CurrencyType QuoteCurrency(MarketQuoteUnit unit) => unit switch
    {
        MarketQuoteUnit.UsCentsPerBushel or MarketQuoteUnit.UsdPerTon => CurrencyType.Usd,
        _ => CurrencyType.Brl
    };

    /// <summary>Valor, na moeda da cotação, de 1 unidade de preço por lote.</summary>
    public static decimal PointValue(MarketQuoteUnit unit, decimal contractSize)
        => unit == MarketQuoteUnit.UsCentsPerBushel ? contractSize * 0.01m : contractSize;

    /// <summary>
    /// Toneladas por lote (sem arredondar). Nulo quando a unidade não tem tonelagem (<c>BrlPerUsd</c>)
    /// ou quando o fator necessário não é positivo.
    /// </summary>
    public static decimal? TonsPerLot(MarketQuoteUnit unit, decimal contractSize, decimal tonFactor) => unit switch
    {
        MarketQuoteUnit.UsCentsPerBushel => tonFactor > 0 ? contractSize / (tonFactor * 100m) : null,
        MarketQuoteUnit.BrlPerBag => tonFactor > 0 ? contractSize / tonFactor : null,
        MarketQuoteUnit.UsdPerTon => contractSize,
        _ => null
    };

    /// <summary>Nocional em US$ por lote; só para o dólar futuro (<c>BrlPerUsd</c>).</summary>
    public static decimal? UsdPerLot(MarketQuoteUnit unit, decimal contractSize)
        => unit == MarketQuoteUnit.BrlPerUsd ? contractSize : null;

    /// <summary>Resultado de um casamento na moeda da cotação, 2 casas (AwayFromZero).</summary>
    public static decimal RealizedPnl(MarketQuoteUnit unit, decimal contractSize, MarketTradeSide openSide,
        decimal openPrice, decimal closePrice, int lots)
    {
        var sign = openSide == MarketTradeSide.Buy ? 1m : -1m;
        var pnl = (closePrice - openPrice) * lots * PointValue(unit, contractSize) * sign;
        return Math.Round(pnl, 2, MidpointRounding.AwayFromZero);
    }

    public static void EnsureTradable(MarketInstrument instrument)
    {
        if (instrument.ContractSize <= 0)
            throw new BusinessException($"O instrumento {instrument.Code} não tem tamanho de lote configurado.");

        var needsTonFactor = instrument.QuoteUnit is MarketQuoteUnit.UsCentsPerBushel or MarketQuoteUnit.BrlPerBag;
        if (needsTonFactor && instrument.TonFactor <= 0)
            throw new BusinessException(
                $"O instrumento {instrument.Code} não tem fator de conversão para tonelada configurado.");
    }
}
