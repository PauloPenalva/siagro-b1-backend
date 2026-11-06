using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>Aritmética pura do hedge de moeda (reaproveitável na marcação a mercado).</summary>
public static class MarketFxMath
{
    /// <summary>
    /// Resultado em R$: venda de US$ = (taxa contratada − taxa de referência) × US$; compra = o inverso.
    /// Arredondado a 2 casas (AwayFromZero).
    /// </summary>
    public static decimal RealizedPnlBrl(MarketTradeSide side, decimal contractedRate, decimal referenceRate,
        decimal amountUsd)
    {
        var sign = side == MarketTradeSide.Sell ? 1m : -1m;
        return Math.Round((contractedRate - referenceRate) * amountUsd * sign, 2, MidpointRounding.AwayFromZero);
    }
}
