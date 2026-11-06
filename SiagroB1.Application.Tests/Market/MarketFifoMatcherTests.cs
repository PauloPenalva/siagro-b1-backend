using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Domain.Enums;
using Xunit;
using static SiagroB1.Application.Services.Market.Hedges.MarketFifoMatcher;

namespace SiagroB1.Application.Tests.Market;

public class MarketFifoMatcherTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private const MarketTradeSide B = MarketTradeSide.Buy;
    private const MarketTradeSide S = MarketTradeSide.Sell;

    private static Leg L(int id, int day, MarketTradeSide side, int lots, decimal price = 100m)
        => new(id, T0.AddDays(day), side, lots, price);

    [Fact]
    public void EmptyList_YieldsEmptyResult()
    {
        var r = Run([]);
        Assert.Empty(r.Matches);
        Assert.Empty(r.OpenLots);
    }

    [Fact]
    public void PartialClose_BuyThenSmallerSell()
    {
        var r = Run([L(1, 0, B, 10, 100m), L(2, 1, S, 4, 110m)]);
        var m = Assert.Single(r.Matches);
        Assert.Equal(new Match(1, 2, 4, 100m, 110m, B), m);
        Assert.Equal(6, r.OpenLots[1]);
        Assert.Equal(0, r.OpenLots[2]);
    }

    [Fact]
    public void Sell_ConsumesOldestBuyFirst()
    {
        var r = Run([L(1, 0, B, 5, 100m), L(2, 1, B, 5, 101m), L(3, 2, S, 7, 105m)]);
        Assert.Equal(2, r.Matches.Count);
        Assert.Equal(new Match(1, 3, 5, 100m, 105m, B), r.Matches[0]);
        Assert.Equal(new Match(2, 3, 2, 101m, 105m, B), r.Matches[1]);
        Assert.Equal(0, r.OpenLots[1]);
        Assert.Equal(3, r.OpenLots[2]);
        Assert.Equal(0, r.OpenLots[3]);
    }

    [Fact]
    public void SellFirstThenBuy_OpeningLegIsTheSell()
    {
        var r = Run([L(1, 0, S, 3, 120m), L(2, 1, B, 10, 118m)]);
        var m = Assert.Single(r.Matches);
        Assert.Equal(new Match(1, 2, 3, 120m, 118m, S), m);
        Assert.Equal(0, r.OpenLots[1]);
        Assert.Equal(7, r.OpenLots[2]);
    }

    [Fact]
    public void PositionFlip_ZeroesEverything()
    {
        var r = Run([L(1, 0, B, 5), L(2, 1, S, 8), L(3, 2, B, 3)]);
        Assert.Equal(2, r.Matches.Count);
        Assert.Equal(new Match(1, 2, 5, 100m, 100m, B), r.Matches[0]);
        Assert.Equal(new Match(2, 3, 3, 100m, 100m, S), r.Matches[1]);
        Assert.Equal(3, r.OpenLots.Count);
        Assert.All(r.OpenLots.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void SameTimestamp_TiesBrokenById()
    {
        var r = Run([new Leg(2, T0, S, 4, 110m), new Leg(1, T0, B, 4, 100m)]);
        var m = Assert.Single(r.Matches);
        Assert.Equal(1, m.OpenTradeId);
        Assert.Equal(2, m.CloseTradeId);
    }

    [Fact]
    public void InputOrderIsIrrelevant()
    {
        var legs = new List<Leg>
        {
            L(1, 0, B, 5, 100m), L(2, 1, B, 5, 101m), L(3, 2, S, 7, 105m), L(4, 3, S, 6, 107m), L(5, 4, B, 2, 99m)
        };
        var expected = Run(legs);
        var rnd = new Random(7);
        for (var i = 0; i < 20; i++)
        {
            var r = Run(legs.OrderBy(_ => rnd.Next()).ToList());
            Assert.Equal(expected.Matches, r.Matches);
            Assert.Equal(expected.OpenLots.OrderBy(k => k.Key), r.OpenLots.OrderBy(k => k.Key));
        }
    }

    [Fact]
    public void LegsWithNonPositiveLots_AreIgnored()
    {
        var r = Run([L(1, 0, B, 0), L(2, 1, S, -3), L(3, 2, B, 2)]);
        Assert.Empty(r.Matches);
        Assert.Equal(2, r.OpenLots[3]);
        Assert.Equal(0, r.OpenLots.GetValueOrDefault(1));
    }

    [Fact]
    public void RandomSequences_PreserveNetPositionAndNeverMatchSameSide()
    {
        var rnd = new Random(12345);
        for (var iter = 0; iter < 200; iter++)
        {
            var n = rnd.Next(0, 25);
            var legs = Enumerable.Range(1, n)
                .Select(id => new Leg(id, T0.AddMinutes(rnd.Next(0, 10)), rnd.Next(2) == 0 ? B : S, rnd.Next(1, 12), rnd.Next(90, 110)))
                .ToList();
            var r = Run(legs);
            var side = legs.ToDictionary(l => l.Id, l => l.Side);

            var openBuy = legs.Where(l => l.Side == B).Sum(l => r.OpenLots[l.Id]);
            var openSell = legs.Where(l => l.Side == S).Sum(l => r.OpenLots[l.Id]);
            var net = legs.Sum(l => l.Side == B ? l.Lots : -l.Lots);
            Assert.Equal(net, openBuy - openSell);
            Assert.True(openBuy == 0 || openSell == 0, "não pode sobrar lote aberto dos dois lados");

            var matched = r.Matches.Sum(m => m.Lots);
            Assert.Equal(legs.Sum(l => l.Lots), openBuy + openSell + 2 * matched);

            foreach (var m in r.Matches)
            {
                Assert.NotEqual(side[m.OpenTradeId], side[m.CloseTradeId]);
                Assert.Equal(side[m.OpenTradeId], m.OpenSide);
                Assert.True(m.Lots > 0);
            }
        }
    }
}
