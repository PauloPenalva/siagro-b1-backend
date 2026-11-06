using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketDailyCloseServiceTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    private static MarketDailyCloseService CloseService(UnitOfWork db) =>
        new(db.Context, new TestLogger<MarketDailyCloseService>());

    [Fact]
    public async Task CloseDay_Precedence()
    {
        var db = await SeedAsync(Instrument("MAN"), Instrument("SET"), Instrument("LST"), Instrument("OFF", active: false), Instrument("NOQ"));
        var quotes = QuoteService(db);
        await quotes.SetManualAsync("MAN", new ManualQuoteInput(1200m, null, null, null, null, null, false), "ana", CancellationToken.None);
        await quotes.ApplyCmaUpdatesAsync([Update("SET", 1150m, settlement: true), Update("LST", 1140m)], CancellationToken.None);
        db.Context.MarketQuotes.Add(new() { InstrumentCode = "OFF", Last = 5m });
        await db.SaveChangesAsync();

        var written = await CloseService(db).CloseDayAsync(Day, CancellationToken.None);

        Assert.Equal(3, written);
        var rows = await db.Context.MarketDailyCloses.AsNoTracking().ToDictionaryAsync(c => c.InstrumentCode);
        Assert.Equal(3, rows.Count);
        Assert.Equal((1200m, MarketQuoteSource.Manual, false, "ana"), (rows["MAN"].Close, rows["MAN"].Source, rows["MAN"].IsSettlement, rows["MAN"].UserName));
        Assert.Equal((1150m, MarketQuoteSource.Cma, true), (rows["SET"].Close, rows["SET"].Source, rows["SET"].IsSettlement));
        Assert.Equal((1140m, MarketQuoteSource.Cma, false), (rows["LST"].Close, rows["LST"].Source, rows["LST"].IsSettlement));
        Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), rows["LST"].Date);
    }

    [Fact]
    public async Task CloseDay_DoesNotOverwrite_ManualClose()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        await QuoteService(db).ApplyCmaUpdatesAsync([Update("ZSF7", 1100m)], CancellationToken.None);
        var closes = CloseService(db);
        await closes.SetManualCloseAsync("ZSF7", Day, 1234m, "ana", CancellationToken.None);

        var written = await closes.CloseDayAsync(Day, CancellationToken.None);

        Assert.Equal(0, written);
        var row = await db.Context.MarketDailyCloses.AsNoTracking().SingleAsync();
        Assert.Equal(1234m, row.Close);
        Assert.Equal(MarketQuoteSource.Manual, row.Source);
    }

    [Fact]
    public async Task CloseDay_IsIdempotent()
    {
        var db = await SeedAsync(Instrument("ZSF7"), Instrument("ZSH7"));
        var quotes = QuoteService(db);
        await quotes.ApplyCmaUpdatesAsync([Update("ZSF7", 1m), Update("ZSH7", 2m)], CancellationToken.None);
        var closes = CloseService(db);

        await closes.CloseDayAsync(Day, CancellationToken.None);
        await quotes.ApplyCmaUpdatesAsync([Update("ZSF7", 3m)], CancellationToken.None);
        await closes.CloseDayAsync(Day, CancellationToken.None);

        var rows = await db.Context.MarketDailyCloses.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(3m, rows.Single(r => r.InstrumentCode == "ZSF7").Close);
    }

    [Fact]
    public async Task SetManualClose_Upserts()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var closes = CloseService(db);

        await closes.SetManualCloseAsync("ZSF7", Day, 10m, "ana", CancellationToken.None);
        await closes.SetManualCloseAsync("ZSF7", Day, 11m, "bia", CancellationToken.None);

        var row = await db.Context.MarketDailyCloses.AsNoTracking().SingleAsync();
        Assert.Equal((11m, "bia", MarketQuoteSource.Manual), (row.Close, row.UserName, row.Source));
    }
    [Fact]
    public async Task CloseDay_DoesNotDowngrade_SettlementToLast()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var quotes = QuoteService(db);
        var closes = CloseService(db);
        await quotes.ApplyCmaUpdatesAsync([Update("ZSF7", 1150m, settlement: true)], CancellationToken.None);
        await closes.CloseDayAsync(Day, CancellationToken.None);

        await quotes.ApplyCmaUpdatesAsync([Update("ZSF7", 1160m)], CancellationToken.None);
        var written = await closes.CloseDayAsync(Day, CancellationToken.None);

        Assert.Equal(0, written);
        var row = await db.Context.MarketDailyCloses.AsNoTracking().SingleAsync();
        Assert.Equal((1150m, true), (row.Close, row.IsSettlement));
    }

    [Fact]
    public async Task CloseDay_Upgrades_LastToSettlement()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var quotes = QuoteService(db);
        var closes = CloseService(db);
        await quotes.ApplyCmaUpdatesAsync([Update("ZSF7", 1140m)], CancellationToken.None);
        await closes.CloseDayAsync(Day, CancellationToken.None);

        await quotes.ApplyCmaUpdatesAsync([Update("ZSF7", 1150m, settlement: true)], CancellationToken.None);
        var written = await closes.CloseDayAsync(Day, CancellationToken.None);

        Assert.Equal(1, written);
        var row = await db.Context.MarketDailyCloses.AsNoTracking().SingleAsync();
        Assert.Equal((1150m, true), (row.Close, row.IsSettlement));
    }

    [Fact]
    public async Task CloseDay_FillsQuotedAt_FromSourceQuote()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var quotedAt = new DateTime(2026, 11, 5, 20, 30, 0, DateTimeKind.Utc);
        await QuoteService(db).ApplyCmaUpdatesAsync(
            [new SiagroB1.Commons.Market.MarketQuoteUpdate("ZSF7", 1m, null, null, null, null, null, null, null, false, quotedAt)],
            CancellationToken.None);

        await CloseService(db).CloseDayAsync(Day, CancellationToken.None);

        Assert.Equal(quotedAt, (await db.Context.MarketDailyCloses.AsNoTracking().SingleAsync()).QuotedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SetManualClose_NotPositive_Throws(int close)
    {
        var db = await SeedAsync(Instrument("ZSF7"));

        await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.BusinessException>(() =>
            CloseService(db).SetManualCloseAsync("ZSF7", Day, close, "ana", CancellationToken.None));
    }

    [Fact]
    public async Task SetManualClose_FutureDate_Throws()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        // 2026-11-06T02:00Z = 05/11 23:00 em São Paulo: 06/11 ainda é futuro.
        var service = new MarketDailyCloseService(db.Context, new TestLogger<MarketDailyCloseService>(),
            new ManualTimeProvider(DateTimeOffset.Parse("2026-11-06T02:00:00Z")));

        await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.BusinessException>(() =>
            service.SetManualCloseAsync("ZSF7", new DateOnly(2026, 11, 6), 10m, "ana", CancellationToken.None));
        await service.SetManualCloseAsync("ZSF7", Day, 10m, "ana", CancellationToken.None);
    }
}
