using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.Market;
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

    [Fact]
    public async Task SetManual_NotifiesStore()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var store = new LiveQuoteStore();
        var service = QuoteService(db, store, "0007");

        await service.SetManualAsync("ZSF7", new ManualQuoteInput(1100m, null, null, null, null, null, false), "tester", CancellationToken.None);
        Assert.Equal(1, store.Get("0007").Version);

        await service.ReleaseManualAsync("ZSF7", CancellationToken.None);
        Assert.Equal(2, store.Get("0007").Version);
        Assert.Equal(0, store.Get("0001").Version);
    }
    [Fact]
    public async Task GetBoard_QuotedAt_IsUtc_AndSerializesWithZ()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var stored = new DateTime(2026, 11, 5, 23, 15, 0, DateTimeKind.Unspecified);
        db.Context.MarketQuotes.Add(new MarketQuote { InstrumentCode = "ZSF7", Last = 1m, QuotedAt = stored, UpdatedAt = stored });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var view = (await QuoteService(db).GetBoardAsync(CancellationToken.None)).Single();

        Assert.Equal(DateTimeKind.Utc, view.QuotedAt!.Value.Kind);
        Assert.Equal(stored.Ticks, view.QuotedAt.Value.Ticks);
        var json = JsonSerializer.Serialize(view, MarketQuotesStreamLoop.JsonOptions);
        Assert.Contains("\"quotedAt\":\"2026-11-05T23:15:00Z\"", json);
    }

    [Fact]
    public async Task ApplyCmaUpdates_ConcurrentManual_KeepsManualQuote()
    {
        var name = Guid.NewGuid().ToString();
        var seed = TestDb.CreateUnitOfWork(name);
        seed.Context.MarketInstruments.Add(Instrument("ZSF7"));
        seed.Context.MarketQuotes.Add(new MarketQuote { InstrumentCode = "ZSF7", Last = 1100m, Source = MarketQuoteSource.Cma });
        await seed.SaveChangesAsync();

        // Entre o carregamento e o SaveChanges do flush da CMA, outro escopo grava o lançamento manual.
        var fired = false;
        var interceptor = new BeforeSaveInterceptor(async () =>
        {
            if (fired) return;
            fired = true;
            var other = TestDb.CreateUnitOfWork(name);
            await QuoteService(other).SetManualAsync("ZSF7", new ManualQuoteInput(1200m, null, null, null, null, null, false), "ana", CancellationToken.None);
        });
        var flushing = TestDb.CreateUnitOfWork(name, interceptor);

        await QuoteService(flushing).ApplyCmaUpdatesAsync([Update("ZSF7", 1188.25m)], CancellationToken.None);

        var quote = await TestDb.CreateUnitOfWork(name).Context.MarketQuotes.AsNoTracking().SingleAsync();
        Assert.True(fired);
        Assert.Equal(1200m, quote.Last);
        Assert.Equal(MarketQuoteSource.Manual, quote.Source);
        Assert.Equal("ana", quote.UserName);
        Assert.True(quote.ManualOverride);
    }

    [Fact]
    public async Task ApplyCmaUpdates_AfterRelease_PartialDeltaKeepsManualLabel()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var service = QuoteService(db);
        await service.SetManualAsync("ZSF7", new ManualQuoteInput(1200m, null, null, null, null, null, false), "ana", CancellationToken.None);
        await service.ReleaseManualAsync("ZSF7", CancellationToken.None);

        await service.ApplyCmaUpdatesAsync(
            [new MarketQuoteUpdate("ZSF7", null, 1199m, null, null, null, null, null, null, false, null)], CancellationToken.None);

        var quote = await db.Context.MarketQuotes.AsNoTracking().SingleAsync();
        Assert.Equal((1200m, 1199m, MarketQuoteSource.Manual, "ana"), (quote.Last, quote.Bid, quote.Source, quote.UserName));

        await service.ApplyCmaUpdatesAsync([Update("ZSF7", 1190m)], CancellationToken.None);

        quote = await db.Context.MarketQuotes.AsNoTracking().SingleAsync();
        Assert.Equal((1190m, MarketQuoteSource.Cma, (string?)null), (quote.Last, quote.Source, quote.UserName));
    }

    [Fact]
    public async Task ApplyCmaUpdates_SymbolMatching_IsCaseInsensitive()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var service = QuoteService(db);

        await service.ApplyCmaUpdatesAsync([Update("zsf7", 10m)], CancellationToken.None);
        await service.ApplyCmaUpdatesAsync([Update("ZSF7", 11m), Update("zsf7", 12m)], CancellationToken.None);

        var quote = await db.Context.MarketQuotes.AsNoTracking().SingleAsync();
        Assert.Equal("ZSF7", quote.InstrumentCode);
        Assert.Equal(12m, quote.Last);
    }

    [Fact]
    public async Task GetBoard_ExposesAskAndUserName()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var service = QuoteService(db);
        await service.SetManualAsync("ZSF7", new ManualQuoteInput(1200m, 1199m, 1201m, null, null, null, false), "ana", CancellationToken.None);

        var view = (await service.GetBoardAsync(CancellationToken.None)).Single();

        Assert.Equal((1201m, "ana"), (view.Ask, view.UserName));
    }

    [Fact]
    public async Task GetBoard_OrdersGroupsBySmallestSortOrder_ThenCode()
    {
        MarketInstrument I(string code, string? group, int order)
        {
            var i = Instrument(code);
            i.GroupName = group;
            i.SortOrder = order;
            return i;
        }

        var db = await SeedAsync(I("B2", "Zeta", 5), I("B1", "Zeta", 5), I("A1", "Alfa", 20), I("Z9", "Zeta", 1), I("N1", null, 50));

        var board = await QuoteService(db).GetBoardAsync(CancellationToken.None);

        Assert.Equal(["Z9", "B1", "B2", "A1", "N1"], board.Select(b => b.Code));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetManual_LastNotPositive_Throws(int last)
    {
        var db = await SeedAsync(Instrument("ZSF7"));

        await Assert.ThrowsAsync<BusinessException>(() => QuoteService(db).SetManualAsync("ZSF7",
            new ManualQuoteInput(last, null, null, null, null, null, false), "ana", CancellationToken.None));
    }

    [Fact]
    public async Task SetManual_NegativePrice_Throws()
    {
        var db = await SeedAsync(Instrument("ZSF7"));

        await Assert.ThrowsAsync<BusinessException>(() => QuoteService(db).SetManualAsync("ZSF7",
            new ManualQuoteInput(10m, -1m, null, null, null, null, false), "ana", CancellationToken.None));
    }

    private sealed class BeforeSaveInterceptor(Func<Task> action) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await action();
            return result;
        }
    }
}
