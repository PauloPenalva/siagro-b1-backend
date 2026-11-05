using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketQuoteServiceTests
{
    [Fact]
    public async Task ApplyCmaUpdates_Upserts_KnownActiveInstrument()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var service = QuoteService(db);

        await service.ApplyCmaUpdatesAsync([Update("ZSF7", 1100m)], CancellationToken.None);
        await service.ApplyCmaUpdatesAsync([Update("ZSF7", 1105.5m, settlement: true)], CancellationToken.None);

        var quote = await db.Context.MarketQuotes.SingleAsync();
        Assert.Equal(1105.5m, quote.Last);
        Assert.True(quote.IsSettlement);
        Assert.Equal(MarketQuoteSource.Cma, quote.Source);
        Assert.Null(quote.UserName);
        Assert.False(quote.ManualOverride);
    }

    [Fact]
    public async Task ApplyCmaUpdates_NullFields_KeepExistingValues()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var service = QuoteService(db);
        await service.ApplyCmaUpdatesAsync(
            [new MarketQuoteUpdate("ZSF7", 1100m, 1099m, 1101m, 0.5m, 1110m, 1090m, 1095m, 1098m, false, null)],
            CancellationToken.None);

        await service.ApplyCmaUpdatesAsync(
            [new MarketQuoteUpdate("ZSF7", null, 1100m, null, null, null, null, null, null, true, null)],
            CancellationToken.None);

        var quote = await db.Context.MarketQuotes.AsNoTracking().SingleAsync();
        Assert.Equal(1100m, quote.Last);
        Assert.Equal(1100m, quote.Bid);
        Assert.Equal(1101m, quote.Ask);
        Assert.Equal(0.5m, quote.ChangePercent);
        Assert.Equal(1110m, quote.High);
        Assert.Equal(1090m, quote.Low);
        Assert.Equal(1095m, quote.Open);
        Assert.Equal(1098m, quote.PreviousClose);
        Assert.False(quote.IsSettlement); // só muda quando Last vem preenchido
    }

    [Fact]
    public async Task ApplyCmaUpdates_Ignores_UnknownOrInactive()
    {
        var db = await SeedAsync(Instrument("ZSF7", active: false));
        var service = QuoteService(db);

        await service.ApplyCmaUpdatesAsync([Update("ZSF7", 1m), Update("XXXX", 2m)], CancellationToken.None);

        Assert.Empty(await db.Context.MarketQuotes.ToListAsync());
    }

    [Fact]
    public async Task ApplyCmaUpdates_DoesNotOverwrite_ManualOverride()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var service = QuoteService(db);
        await service.SetManualAsync("ZSF7", new ManualQuoteInput(1200m, null, null, null, null, null, false), "ana", CancellationToken.None);

        await service.ApplyCmaUpdatesAsync([Update("ZSF7", 1188.25m)], CancellationToken.None);

        var quote = await db.Context.MarketQuotes.AsNoTracking().SingleAsync();
        Assert.Equal(1200m, quote.Last);
        Assert.Equal(MarketQuoteSource.Manual, quote.Source);
        Assert.Equal("ana", quote.UserName);
        Assert.True(quote.ManualOverride);
    }

    [Fact]
    public async Task ReleaseManual_ThenCmaOverwrites()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var service = QuoteService(db);
        await service.SetManualAsync("ZSF7", new ManualQuoteInput(1200m, null, null, null, null, null, false), "ana", CancellationToken.None);

        await service.ReleaseManualAsync("ZSF7", CancellationToken.None);
        await service.ApplyCmaUpdatesAsync([Update("ZSF7", 1188.25m)], CancellationToken.None);

        var quote = await db.Context.MarketQuotes.AsNoTracking().SingleAsync();
        Assert.Equal(1188.25m, quote.Last);
        Assert.Equal(MarketQuoteSource.Cma, quote.Source);
        Assert.Null(quote.UserName);
        Assert.False(quote.ManualOverride);
    }

    [Fact]
    public async Task SetManual_UnknownOrInactive_Throws()
    {
        var db = await SeedAsync(Instrument("OFF", active: false));
        var service = QuoteService(db);
        var input = new ManualQuoteInput(1m, null, null, null, null, null, false);

        await Assert.ThrowsAsync<BusinessException>(() => service.SetManualAsync("OFF", input, "ana", CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() => service.SetManualAsync("NOPE", input, "ana", CancellationToken.None));
    }

    [Fact]
    public async Task GetBoard_ExcludesInactive_ButDailyClosesRemain()
    {
        var db = await SeedAsync(Instrument("ZSF7"), Instrument("ZSH7"));
        var quotes = QuoteService(db);
        await quotes.ApplyCmaUpdatesAsync([Update("ZSF7", 10m), Update("ZSH7", 20m)], CancellationToken.None);
        var closes = new MarketDailyCloseService(db.Context, new TestLogger<MarketDailyCloseService>());
        await closes.CloseDayAsync(new DateOnly(2026, 11, 5), CancellationToken.None);
        Assert.Equal(2, (await quotes.GetBoardAsync(CancellationToken.None)).Count);

        var inactive = await db.Context.MarketInstruments.SingleAsync(i => i.Code == "ZSH7");
        inactive.Active = false;
        await db.SaveChangesAsync();

        var board = await quotes.GetBoardAsync(CancellationToken.None);
        Assert.Equal(["ZSF7"], board.Select(b => b.Code));
        Assert.Equal(10m, board[0].Last);
        Assert.Equal(2, await db.Context.MarketDailyCloses.CountAsync());
    }
}
