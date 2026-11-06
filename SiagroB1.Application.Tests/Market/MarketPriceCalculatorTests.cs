using SiagroB1.Application.Services.Market.Pricing;
using SiagroB1.Domain.Enums;
using Xunit;

namespace SiagroB1.Application.Tests.Market;

public class MarketPriceCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static MarketPriceInputs S1() => new(1188.25m, MarketQuoteUnit.UsCentsPerBushel, 0.36745m, 50m, 8m,
        5.4000m, 5.4650m, 30, 0.02m, Today, new(2027, 3, 15), new(2027, 4, 15),
        0.5m, true, 1.5m, 0.033m, 0.3m, 1.5m, 180m, 10m);

    private static MarketPriceInputs S2() => new(1050.50m, MarketQuoteUnit.UsCentsPerBushel, 0.36745m, -20m, 12.5m,
        5.3125m, 5.4010m, 60, 0m, Today, new(2027, 5, 20), new(2027, 5, 5),
        1.20m, false, 2m, 0.04m, 0.25m, 0m, 95.50m, 0m);

    private static MarketPriceInputs M1() => new(430.75m, MarketQuoteUnit.UsCentsPerBushel, 0.393678m, 85m, 10m,
        5.4000m, 5.4320m, 30, 0.015m, Today, new(2027, 7, 1), new(2027, 7, 31),
        1m, true, 2m, 0.03m, 0.25m, 1.5m, 150m, 5m);

    private static MarketPriceInputs B1() => new(68.50m, MarketQuoteUnit.BrlPerBag, 1m, 1.50m, 0m,
        null, null, null, 0m, Today, new(2027, 7, 1), new(2027, 7, 31),
        1.00m, false, 2m, 0.03m, 0.25m, 0m, 120m, 5m);

    [Fact]
    public void S1_MatchesOracle()
    {
        var r = MarketPriceCalculator.Calculate(S1());
        Assert.Equal(MarketPriceStatus.Ok, r.Status);
        Assert.Equal(5.7267m, r.NetFx);
        Assert.Equal(153.588363m, Math.Round(r.PortPrice!.Value, 6, MidpointRounding.AwayFromZero));
        Assert.Equal(11.88m, r.FreightPerBag);
        Assert.Equal(137.67m, r.Price);
        Assert.Equal(1.57m, r.Steps.Single(s => s.Key == "interest").Value);
    }

    [Fact]
    public void S2_MatchesOracle()
    {
        var r = MarketPriceCalculator.Calculate(S2());
        Assert.Equal(5.5780m, r.NetFx);
        Assert.Equal(5.73m, r.FreightPerBag);
        Assert.Equal(112.15m, r.Price);
        Assert.Equal(-0.74m, r.Steps.Single(s => s.Key == "interest").Value);
    }

    [Fact]
    public void M1_MatchesOracle()
    {
        var r = MarketPriceCalculator.Calculate(M1());
        Assert.Equal(5.6699m, r.NetFx);
        Assert.Equal(9.45m, r.FreightPerBag);
        Assert.Equal(53.87m, r.Price);
        Assert.Equal(0.59m, r.Steps.Single(s => s.Key == "interest").Value);
    }

    [Fact]
    public void B1_MatchesOracle()
    {
        var r = MarketPriceCalculator.Calculate(B1());
        Assert.Equal(1m, r.NetFx);
        Assert.Equal(70.00m, r.PortPrice);
        Assert.Equal(7.56m, r.FreightPerBag);
        Assert.Equal(60.52m, r.Price);
        Assert.Equal(0.63m, r.Steps.Single(s => s.Key == "interest").Value);
        Assert.DoesNotContain(r.Steps, s => s.Key is "perTon" or "netFx");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void MissingQuote_HasNoPrice(int? quote)
    {
        var r = MarketPriceCalculator.Calculate(S1() with { Quote = quote });
        Assert.Equal(MarketPriceStatus.MissingQuote, r.Status);
        Assert.Null(r.Price);
    }

    [Fact]
    public void MissingFx_WhenSpotCurveOrDaysAbsent()
    {
        foreach (var i in new[]
                 {
                     S1() with { SpotFx = null }, S1() with { CurveFx = null },
                     S1() with { CurveDays = null }, S1() with { CurveDays = 0 },
                 })
        {
            var r = MarketPriceCalculator.Calculate(i);
            Assert.Equal(MarketPriceStatus.MissingFx, r.Status);
            Assert.Null(r.Price);
            Assert.Null(r.NetFx);
        }
    }

    [Fact]
    public void MissingFreight_KeepsPortPrice()
    {
        var r = MarketPriceCalculator.Calculate(S1() with { FreightPerTon = null });
        Assert.Equal(MarketPriceStatus.MissingFreight, r.Status);
        Assert.Null(r.Price);
        Assert.NotNull(r.PortPrice);
        Assert.Equal(5.7267m, r.NetFx);
    }

    [Theory]
    [InlineData(MarketQuoteUnit.BrlPerUsd, 0.36745)]
    [InlineData(MarketQuoteUnit.UsdPerTon, 0)]
    public void InvalidSetup(MarketQuoteUnit unit, double factor)
    {
        var r = MarketPriceCalculator.Calculate(S1() with { QuoteUnit = unit, TonFactor = (decimal)factor });
        Assert.Equal(MarketPriceStatus.InvalidSetup, r.Status);
        Assert.Null(r.Price);
    }

    [Fact]
    public void UsdPerTon_WithFactorOne()
    {
        var i = S1() with { QuoteUnit = MarketQuoteUnit.UsdPerTon, TonFactor = 1m, Quote = 400m, Premium = 0m, ElevationCost = 0m };
        var r = MarketPriceCalculator.Calculate(i);
        Assert.Equal(MarketPriceStatus.Ok, r.Status);
        Assert.Equal(400m * r.NetFx!.Value * 0.06m, r.PortPrice!.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Steps_AddUpToPrice(int scenario)
    {
        var r = MarketPriceCalculator.Calculate(scenario switch { 0 => S1(), 1 => S2(), 2 => M1(), _ => B1() });
        decimal V(string k) => r.Steps.Single(s => s.Key == k).Value;
        var sum = V("portPrice") - V("discount") - V("margin") + V("interest") - V("technicalLoss") - V("freight") - V("ruralTax");
        Assert.InRange(Math.Abs(sum - r.Price!.Value), 0m, 0.01m);
        Assert.Equal(r.Price, V("price"));
    }

    [Fact]
    public void NeverThrows_OnExtremeInputs()
    {
        var i = S1() with
        {
            MarginPercent = 100m, TechnicalLossPercent = 100m, RuralTaxPercent = 100m, DiscountValue = 100m,
            ReceiptDate = new(2027, 4, 15), PaymentDate = new(2026, 1, 1),
        };
        var r = MarketPriceCalculator.Calculate(i);
        Assert.NotNull(r);
        var huge = MarketPriceCalculator.Calculate(S1() with { Quote = decimal.MaxValue, Premium = 1m });
        Assert.NotNull(huge);
    }
}
