using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using Xunit;

namespace SiagroB1.Application.Tests.Market;

public class MarketUnitsTests
{
    [Theory]
    [InlineData(MarketQuoteUnit.UsCentsPerBushel, CurrencyType.Usd)]
    [InlineData(MarketQuoteUnit.UsdPerTon, CurrencyType.Usd)]
    [InlineData(MarketQuoteUnit.BrlPerBag, CurrencyType.Brl)]
    [InlineData(MarketQuoteUnit.BrlPerUsd, CurrencyType.Brl)]
    public void QuoteCurrency_FollowsUnit(MarketQuoteUnit unit, CurrencyType expected)
        => Assert.Equal(expected, MarketUnits.QuoteCurrency(unit));

    [Fact]
    public void PointValue_CentsPerBushelScalesByCent()
        => Assert.Equal(50m, MarketUnits.PointValue(MarketQuoteUnit.UsCentsPerBushel, 5000m));

    [Theory]
    [InlineData(MarketQuoteUnit.BrlPerBag)]
    [InlineData(MarketQuoteUnit.UsdPerTon)]
    [InlineData(MarketQuoteUnit.BrlPerUsd)]
    public void PointValue_OtherUnitsUseContractSize(MarketQuoteUnit unit)
        => Assert.Equal(450m, MarketUnits.PointValue(unit, 450m));

    [Fact]
    public void TonsPerLot_SoyCbot()
        => Assert.Equal(136.0729m, Math.Round(MarketUnits.TonsPerLot(MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m)!.Value, 4));

    [Fact]
    public void TonsPerLot_CornB3()
        => Assert.Equal(27m, Math.Round(MarketUnits.TonsPerLot(MarketQuoteUnit.BrlPerBag, 450m, 16.666667m)!.Value, 4));

    [Fact]
    public void TonsPerLot_UsdPerTonIsContractSize()
        => Assert.Equal(100m, MarketUnits.TonsPerLot(MarketQuoteUnit.UsdPerTon, 100m, 0m));

    [Fact]
    public void TonsPerLot_NullForBrlPerUsd()
        => Assert.Null(MarketUnits.TonsPerLot(MarketQuoteUnit.BrlPerUsd, 50000m, 1m));

    [Theory]
    [InlineData(MarketQuoteUnit.UsCentsPerBushel, 0)]
    [InlineData(MarketQuoteUnit.UsCentsPerBushel, -1)]
    [InlineData(MarketQuoteUnit.BrlPerBag, 0)]
    public void TonsPerLot_NullWhenFactorNotPositive(MarketQuoteUnit unit, int factor)
        => Assert.Null(MarketUnits.TonsPerLot(unit, 5000m, factor));

    [Fact]
    public void UsdPerLot_OnlyForBrlPerUsd()
    {
        Assert.Equal(50000m, MarketUnits.UsdPerLot(MarketQuoteUnit.BrlPerUsd, 50000m));
        Assert.Null(MarketUnits.UsdPerLot(MarketQuoteUnit.UsCentsPerBushel, 5000m));
        Assert.Null(MarketUnits.UsdPerLot(MarketQuoteUnit.BrlPerBag, 450m));
        Assert.Null(MarketUnits.UsdPerLot(MarketQuoteUnit.UsdPerTon, 100m));
    }

    [Fact]
    public void RealizedPnl_SoyBuyOpen()
        => Assert.Equal(6250.00m, MarketUnits.RealizedPnl(MarketQuoteUnit.UsCentsPerBushel, 5000m, MarketTradeSide.Buy, 1180.00m, 1192.50m, 10));

    [Fact]
    public void RealizedPnl_CornSellOpen()
        => Assert.Equal(3375.00m, MarketUnits.RealizedPnl(MarketQuoteUnit.BrlPerBag, 450m, MarketTradeSide.Sell, 68.00m, 66.50m, 5));

    [Fact]
    public void RealizedPnl_DollarFutureSellOpen()
        => Assert.Equal(5000.00m, MarketUnits.RealizedPnl(MarketQuoteUnit.BrlPerUsd, 50000m, MarketTradeSide.Sell, 5.4500m, 5.4000m, 2));

    [Fact]
    public void RealizedPnl_RoundsToTwoPlacesAwayFromZero()
    {
        // 0,005 vira 0,01; o negativo vira -0,01
        Assert.Equal(0.01m, MarketUnits.RealizedPnl(MarketQuoteUnit.BrlPerBag, 1m, MarketTradeSide.Buy, 1.000m, 1.005m, 1));
        Assert.Equal(-0.01m, MarketUnits.RealizedPnl(MarketQuoteUnit.BrlPerBag, 1m, MarketTradeSide.Sell, 1.000m, 1.005m, 1));
    }

    private static MarketInstrument Instrument(MarketQuoteUnit unit, decimal size, decimal ton) => new()
    {
        Code = "ZSF27", Description = "Soja", Commodity = "SOY", QuoteUnit = unit, ContractSize = size, TonFactor = ton
    };

    [Fact]
    public void EnsureTradable_RejectsZeroContractSize()
    {
        var ex = Assert.Throws<BusinessException>(() => MarketUnits.EnsureTradable(Instrument(MarketQuoteUnit.UsCentsPerBushel, 0m, 0.36745m)));
        Assert.Equal("O instrumento ZSF27 não tem tamanho de lote configurado.", ex.Message);
    }

    [Fact]
    public void EnsureTradable_RejectsZeroTonFactorWhenUnitNeedsIt()
    {
        var ex = Assert.Throws<BusinessException>(() => MarketUnits.EnsureTradable(Instrument(MarketQuoteUnit.BrlPerBag, 450m, 0m)));
        Assert.Equal("O instrumento ZSF27 não tem fator de conversão para tonelada configurado.", ex.Message);
    }

    [Theory]
    [InlineData(MarketQuoteUnit.UsdPerTon)]
    [InlineData(MarketQuoteUnit.BrlPerUsd)]
    public void EnsureTradable_IgnoresTonFactorWhenUnitDoesNotNeedIt(MarketQuoteUnit unit)
        => MarketUnits.EnsureTradable(Instrument(unit, 100m, 0m));

    [Fact]
    public void EnsureTradable_AcceptsValidInstrument()
        => MarketUnits.EnsureTradable(Instrument(MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m));
}
