using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketFuturesTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketDerivativesPositionServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static DateTime At(int day, int hour) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OpenPositions_WeightedAverageAndSignedTons()
    {
        var db = await SeedAsync();
        var svc = TradeService(db);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 2, 1180m, tradedAt: At(3, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 2, 1200m, tradedAt: At(4, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Sell, 1, 1210m, tradedAt: At(5, 14)), "u", Ct); // fecha 1 de 1180
        await svc.CreateAsync(Input(MarketTradeSide.Sell, 3, 68m, Corn, tradedAt: At(5, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Sell, 2, 5.45m, Dollar, tradedAt: At(5, 14)), "u", Ct);
        db.Context.MarketQuotes.Add(new MarketQuote { InstrumentCode = Soy, Last = 1220m });
        await db.SaveChangesAsync();

        var rows = await PositionService(db).GetOpenPositionsAsync(Ct);

        var soy = rows.Single(r => r.InstrumentCode == Soy);
        Assert.Equal(3, soy.NetLots);
        Assert.Equal(1193.333333m, Math.Round(soy.AvgPrice, 6));
        Assert.Equal(408.2188m, Math.Round(soy.Tons!.Value, 4));
        Assert.Null(soy.Usd);
        Assert.Equal(1220m, soy.LastPrice);
        Assert.Equal(4000m, soy.UnrealizedPnl); // (1220 − 1193,33) × 3 × 50
        Assert.Equal("XP Corretora – C-001", soy.AccountLabel);
        Assert.Equal(CurrencyType.Usd, soy.Currency);
        Assert.Equal(new DateOnly(2027, 1, 14), soy.ExpirationDate);

        var corn = rows.Single(r => r.InstrumentCode == Corn);
        Assert.Equal(-3, corn.NetLots);
        Assert.Equal(-81m, Math.Round(corn.Tons!.Value, 4));
        Assert.Null(corn.UnrealizedPnl);
        Assert.Null(corn.LastPrice);

        var dollar = rows.Single(r => r.InstrumentCode == Dollar);
        Assert.Equal(-100000m, dollar.Usd);
        Assert.Null(dollar.Tons);
    }

    [Fact]
    public async Task OpenPositions_ShortUnrealizedSign()
    {
        var db = await SeedAsync();
        await TradeService(db).CreateAsync(Input(MarketTradeSide.Sell, 5, 68m, Corn), "u", Ct);
        db.Context.MarketQuotes.Add(new MarketQuote { InstrumentCode = Corn, Last = 66.5m });
        await db.SaveChangesAsync();
        var row = Assert.Single(await PositionService(db).GetOpenPositionsAsync(Ct));
        Assert.Equal(3375m, row.UnrealizedPnl);
        Assert.Equal(CurrencyType.Brl, row.Currency);
    }

    [Fact]
    public async Task Realized_FilteredByPeriod()
    {
        var db = await SeedAsync();
        var svc = TradeService(db);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 10, 1180m, tradedAt: At(2, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Sell, 4, 1192.5m, tradedAt: At(3, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Sell, 6, 1192.5m, tradedAt: At(5, 14)), "u", Ct);

        var rows = await PositionService(db).GetRealizedAsync(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 6), Ct);

        var row = Assert.Single(rows);
        Assert.Equal(6, row.Lots);
        Assert.Equal(3750m, row.RealizedPnl);
        Assert.Equal(new DateOnly(2026, 11, 5), row.CloseDate);
        Assert.Equal("XP Corretora – C-001", row.AccountLabel);
        Assert.Equal(1180m, row.OpenPrice);
        Assert.Equal(1192.5m, row.ClosePrice);
    }

    [Fact]
    public async Task Realized_InvertedPeriod_BusinessException()
    {
        var db = await SeedAsync();
        await Assert.ThrowsAsync<BusinessException>(() =>
            PositionService(db).GetRealizedAsync(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 1), Ct));
    }
}
