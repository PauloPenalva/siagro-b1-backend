using SiagroB1.Application.Services.Market.Position;
using Xunit;

namespace SiagroB1.Application.Tests.Market;

public class MarketPositionMathTests
{
    private static readonly MarketPositionMath.MonthRule[] Soy = MarketPositionMath.DefaultRules(new[] { 1, 3, 5, 7, 8, 9, 11 }).ToArray();

    [Fact]
    public void MapDelivery_JuneGoesToJuly()
        => Assert.Equal(new MarketPositionMath.FuturesMonth(2027, 7), MarketPositionMath.MapDelivery(new DateOnly(2027, 6, 20), Soy));

    [Fact]
    public void MapDelivery_DecemberRollsToNextYearJanuary()
        => Assert.Equal(new MarketPositionMath.FuturesMonth(2028, 1), MarketPositionMath.MapDelivery(new DateOnly(2027, 12, 10), Soy));

    [Fact]
    public void MapDelivery_ExactMonthStaysInYear()
        => Assert.Equal(new MarketPositionMath.FuturesMonth(2027, 7), MarketPositionMath.MapDelivery(new DateOnly(2027, 7, 1), Soy));

    [Fact]
    public void MapDelivery_WithoutRuleForMonth_ReturnsNull()
        => Assert.Null(MarketPositionMath.MapDelivery(new DateOnly(2027, 6, 20),
            new[] { new MarketPositionMath.MonthRule(5, 5, 0) }));

    [Theory]
    [InlineData(600000, 0, 0, 0, 600000, 600000)]      // fixo sem entrega
    [InlineData(600000, 200000, 0, 0, 600000, 400000)] // fixo com entrega parcial
    [InlineData(600000, 100000, 0, 0, 300000, 200000)] // PAF parcialmente fixado e entregue
    [InlineData(600000, 350000, 0, 0, 300000, 0)]      // entregue além do fixado
    [InlineData(600000, 0, 50000, 0, 600000, 550000)]  // washout fixado reduz o firme
    [InlineData(600000, 0, 50000, 50000, 300000, 300000)] // washout não fixado não consome o firme
    [InlineData(600000, 700000, 0, 0, 600000, 0)]      // nunca negativo
    [InlineData(600000, 0, 0, 0, 900000, 600000)]      // nunca maior que o saldo
    public void FirmOpenKg_Cases(decimal total, decimal allocated, decimal washed, decimal washedUnfixed, decimal confirmed, decimal expected)
        => Assert.Equal(expected, MarketPositionMath.FirmOpenKg(total, allocated, washed, washedUnfixed, confirmed));

    [Theory]
    [InlineData(1000, -600, 60)]
    [InlineData(1000, -1500, 100)]
    [InlineData(1000, 200, 0)]
    [InlineData(-500, 500, 100)]
    public void HedgePercent_Cases(decimal physical, decimal hedge, decimal expected)
        => Assert.Equal(expected, MarketPositionMath.HedgePercent(physical, hedge));

    [Fact]
    public void HedgePercent_ZeroPhysical_ReturnsNull()
        => Assert.Null(MarketPositionMath.HedgePercent(0, -100));

    [Fact]
    public void Covered_LimitedByPhysicalAndOppositeDirection()
    {
        Assert.Equal(600, MarketPositionMath.Covered(1000, -600));
        Assert.Equal(1000, MarketPositionMath.Covered(1000, -1500));
        Assert.Equal(0, MarketPositionMath.Covered(1000, 200));
        Assert.Equal(0, MarketPositionMath.Covered(0, -200));
    }

    [Theory]
    [InlineData(1200, 1000, null, MarketPositionMath.LimitState.LongExceeded)]
    [InlineData(-800, null, 500, MarketPositionMath.LimitState.ShortExceeded)]
    [InlineData(5000, null, null, MarketPositionMath.LimitState.Ok)]
    [InlineData(1000, 1000, 500, MarketPositionMath.LimitState.Ok)]
    [InlineData(-500, 1000, 500, MarketPositionMath.LimitState.Ok)]
    public void Evaluate_Cases(decimal net, int? maxLong, int? maxShort, MarketPositionMath.LimitState expected)
        => Assert.Equal(expected, MarketPositionMath.Evaluate(net, maxLong, maxShort));

    [Fact]
    public void DefaultRules_CornCbot()
    {
        var rules = MarketPositionMath.DefaultRules(new[] { 3, 5, 7, 9, 12 });
        Assert.Equal(12, rules.Count);
        Assert.Equal(new MarketPositionMath.MonthRule(10, 12, 0), rules.Single(r => r.DeliveryMonth == 10));
        Assert.Equal(new MarketPositionMath.MonthRule(1, 3, 0), rules.Single(r => r.DeliveryMonth == 1));
    }

    [Fact]
    public void DefaultRules_AfterLastMonthRollsToNextYear()
    {
        var rules = MarketPositionMath.DefaultRules(new[] { 1, 3, 5, 7, 8, 9, 11 });
        Assert.Equal(new MarketPositionMath.MonthRule(12, 1, 1), rules.Single(r => r.DeliveryMonth == 12));
    }

    [Fact]
    public void DefaultRules_Empty_ReturnsEmpty()
        => Assert.Empty(MarketPositionMath.DefaultRules(Array.Empty<int>()));
}
