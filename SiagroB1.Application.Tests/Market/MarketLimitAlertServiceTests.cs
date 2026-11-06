using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

public class MarketLimitAlertServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));

    private static async Task<UnitOfWork> SeedAsync(UnitOfWork? db = null, decimal purchaseKg = 1_234_500m)
    {
        db ??= TestDb.CreateUnitOfWork(Guid.NewGuid().ToString());
        db.Context.MarketCommodities.Add(new MarketCommodity { Code = "SOY", Name = "Soja" });
        db.Context.MarketCommodityItems.Add(new MarketCommodityItem { CommodityCode = "SOY", ItemCode = "SOJA" });
        db.Context.MarketCommodityMonths.Add(new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = 6, FuturesMonth = 7 });
        db.Context.MarketTradingSettings.Add(new MarketTradingSettings());
        if (purchaseKg > 0) Purchase(db, "PC-1", purchaseKg);
        await db.SaveChangesAsync();
        return db;
    }

    private static void Purchase(UnitOfWork db, string code, decimal kg) =>
        db.Context.PurchaseContracts.Add(new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "F001", CardName = "PRODUTOR", ItemCode = "SOJA", BranchCode = "01",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "01", Type = ContractType.Fixed,
            Status = ContractStatus.Approved, TotalVolume = kg, StandardPrice = 2m, StandardCurrency = CurrencyType.Brl,
            DeliveryEndDate = new DateTime(2027, 6, 30),
        });

    private static MarketExposureLimit Limit(int id, decimal? maxLong, decimal? maxShort = null, int? year = null, int? month = null,
        bool active = true) => new()
    {
        Id = id, Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", Year = year, Month = month,
        MaxLong = maxLong, MaxShort = maxShort, Active = active,
    };

    private static MarketLimitAlertService Service(UnitOfWork db, MarketExposureCache? cache = null)
    {
        var clock = Clock();
        var physical = new MarketPhysicalExposureService(db.Context, cache ?? new MarketExposureCache(clock),
            new FakeCompanyContext("0001"), clock);
        var position = new MarketPositionService(db.Context, physical, new MarketDerivativesPositionService(db.Context), clock);
        return new MarketLimitAlertService(db.Context, position, clock, NullLogger<MarketLimitAlertService>.Instance);
    }

    private static async Task<int> CheckAsync(UnitOfWork db, DateOnly? day = null)
    {
        db.Context.ChangeTracker.Clear();
        return await Service(db).CheckAsync(day ?? Today, Ct);
    }

    [Fact]
    public async Task Check_Exceeded_WritesOneBreachAndOneOutboxMessage()
    {
        var db = await SeedAsync();
        db.Context.MarketExposureLimits.Add(Limit(7, 1000m));
        await db.SaveChangesAsync();

        var written = await CheckAsync(db);

        Assert.Equal(1, written);
        var breach = await db.Context.MarketLimitBreaches.AsNoTracking().SingleAsync();
        Assert.Equal(7, breach.LimitId);
        Assert.Equal(new DateTime(2026, 11, 6), breach.Date);
        Assert.Equal(1234.5m, breach.Value);
        Assert.Equal(1000m, breach.LimitValue);
        var message = await db.Context.NotificationOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationDocumentType.MarketPosition, message.DocumentType);
        Assert.Equal(NotificationEventType.ExposureLimitExceeded, message.EventType);
        Assert.Equal(NotificationOutboxStatus.Pending, message.Status);
        Assert.Equal("LIMITE-7", message.DocumentCode);
        Assert.NotEqual(Guid.Empty, message.DocumentKey);
        var text = JsonDocument.Parse(message.PayloadJson).RootElement.GetProperty("text").GetString();
        Assert.Equal("Limite de exposição excedido — Soja — total: posição comprada de 1.234,500 t acima do limite de 1.000,000 t.", text);
    }

    [Fact]
    public async Task Check_SameDayTwice_DoesNotDuplicate_NextDayWritesAnother()
    {
        var db = await SeedAsync();
        db.Context.MarketExposureLimits.Add(Limit(7, 1000m));
        await db.SaveChangesAsync();

        Assert.Equal(1, await CheckAsync(db));
        Assert.Equal(0, await CheckAsync(db));
        Assert.Single(db.Context.MarketLimitBreaches);
        Assert.Single(db.Context.NotificationOutboxMessages);

        Assert.Equal(1, await CheckAsync(db, Today.AddDays(1)));
        Assert.Equal(2, await db.Context.MarketLimitBreaches.CountAsync());
        Assert.Equal(2, await db.Context.NotificationOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Check_BackToNormal_WritesNothing_AndKeepsEarlierBreaches()
    {
        var db = await SeedAsync();
        db.Context.MarketExposureLimits.Add(Limit(7, 1000m));
        await db.SaveChangesAsync();
        Assert.Equal(1, await CheckAsync(db));

        var limit = await db.Context.MarketExposureLimits.SingleAsync();
        limit.MaxLong = 5000m;
        await db.SaveChangesAsync();

        Assert.Equal(0, await CheckAsync(db, Today.AddDays(1)));
        Assert.Single(db.Context.MarketLimitBreaches);
        Assert.Single(db.Context.NotificationOutboxMessages);
    }

    [Fact]
    public async Task Check_InactiveLimit_WritesNothing()
    {
        var db = await SeedAsync();
        db.Context.MarketExposureLimits.Add(Limit(7, 1000m, active: false));
        await db.SaveChangesAsync();

        Assert.Equal(0, await CheckAsync(db));
        Assert.Empty(db.Context.MarketLimitBreaches);
        Assert.Empty(db.Context.NotificationOutboxMessages);
    }

    [Fact]
    public async Task Check_BucketLimit_UsesMonthDescription()
    {
        var db = await SeedAsync();
        db.Context.MarketExposureLimits.Add(Limit(3, 1000m, year: 2027, month: 7));
        await db.SaveChangesAsync();

        Assert.Equal(1, await CheckAsync(db));
        var message = await db.Context.NotificationOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal("LIMITE-3", message.DocumentCode);
        Assert.Contains("Soja — 07/2027: posição comprada", JsonDocument.Parse(message.PayloadJson).RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Check_IgnoresThePhysicalCache()
    {
        var db = await SeedAsync(purchaseKg: 0);
        db.Context.MarketExposureLimits.Add(Limit(7, 1000m));
        await db.SaveChangesAsync();
        var cache = new MarketExposureCache(Clock());

        // Prime o cache com a posição vazia (sem contratos); depois entra o contrato que estoura.
        var clock = Clock();
        await new MarketPhysicalExposureService(db.Context, cache, new FakeCompanyContext("0001"), clock).GetAsync(Ct);
        Purchase(db, "PC-1", 1_234_500m);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        Assert.Equal(1, await Service(db, cache).CheckAsync(Today, Ct));
    }

    [Fact]
    public async Task Check_LostRaceOnUniqueIndex_DoesNotThrowNorDuplicate_OtherLimitsStillWritten()
    {
        var name = Guid.NewGuid().ToString();
        await SeedAsync(TestDb.CreateUnitOfWork(name));
        var seed = TestDb.CreateUnitOfWork(name);
        seed.Context.MarketExposureLimits.AddRange(Limit(7, 1000m), Limit(8, 1000m, year: 2027, month: 7));
        await seed.SaveChangesAsync();

        // Simula outro job que gravou o estouro do limite 7 entre a consulta e o SaveChanges (o InMemory não aplica índice único).
        var failed = false;
        var db = TestDb.CreateUnitOfWork(name, new FailingSaveInterceptor(ctx =>
        {
            if (failed) return false;
            var racing = ctx.ChangeTracker.Entries<MarketLimitBreach>().Any(e => e.State == EntityState.Added && e.Entity.LimitId == 7);
            if (racing)
            {
                var other = TestDb.CreateUnitOfWork(name);
                other.Context.MarketLimitBreaches.Add(new MarketLimitBreach
                {
                    LimitId = 7, Date = new DateTime(2026, 11, 6), Value = 1234.5m, LimitValue = 1000m, DetectedAt = DateTime.UtcNow,
                });
                other.Context.SaveChanges();
            }
            failed = racing;
            return racing;
        }));

        var written = await Service(db).CheckAsync(Today, Ct);

        Assert.True(failed);
        Assert.Equal(1, written);
        var check = TestDb.CreateUnitOfWork(name);
        Assert.Equal([7, 8], await check.Context.MarketLimitBreaches.AsNoTracking().OrderBy(b => b.LimitId).Select(b => b.LimitId).ToListAsync());
        var message = await check.Context.NotificationOutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal("LIMITE-8", message.DocumentCode);
    }

    [Fact]
    public async Task Check_RealSaveFailure_Throws_AfterWritingOtherLimits()
    {
        var name = Guid.NewGuid().ToString();
        await SeedAsync(TestDb.CreateUnitOfWork(name));
        var seed = TestDb.CreateUnitOfWork(name);
        seed.Context.MarketExposureLimits.AddRange(Limit(7, 1000m), Limit(8, 1000m, year: 2027, month: 7));
        await seed.SaveChangesAsync();

        // Falha de gravação SEM linha concorrente (ex.: FK, truncamento): não é corrida.
        var db = TestDb.CreateUnitOfWork(name, new FailingSaveInterceptor(ctx =>
            ctx.ChangeTracker.Entries<MarketLimitBreach>().Any(e => e.State == EntityState.Added && e.Entity.LimitId == 7)));

        await Assert.ThrowsAsync<AggregateException>(() => Service(db).CheckAsync(Today, Ct));

        var check = TestDb.CreateUnitOfWork(name);
        Assert.Equal(8, (await check.Context.MarketLimitBreaches.AsNoTracking().SingleAsync()).LimitId);
        Assert.Equal("LIMITE-8", (await check.Context.NotificationOutboxMessages.AsNoTracking().SingleAsync()).DocumentCode);
    }

    [Fact]
    public async Task Job_UsesTodayInSaoPaulo()
    {
        var db = await SeedAsync();
        db.Context.MarketExposureLimits.Add(Limit(7, 1000m));
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // 01:30 UTC de 07/11 = 22:30 de 06/11 em São Paulo.
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 7, 1, 30, 0, TimeSpan.Zero));
        await new MarketLimitAlertJob(Service(db), clock, NullLogger<MarketLimitAlertJob>.Instance).ExecuteAsync(Ct);

        Assert.Equal(new DateTime(2026, 11, 6), (await db.Context.MarketLimitBreaches.AsNoTracking().SingleAsync()).Date);
    }

    [Theory]
    [InlineData("commodity", "Soja — 07/2027", 1234.5, 1000, "longExceeded",
        "Limite de exposição excedido — Soja — 07/2027: posição comprada de 1.234,500 t acima do limite de 1.000,000 t.")]
    [InlineData("commodity", "Soja — 07/2027", -1234.5, 1000, "shortExceeded",
        "Limite de exposição excedido — Soja — 07/2027: posição vendida de 1.234,500 t acima do limite de 1.000,000 t.")]
    [InlineData("usd", "Dólar — total", 1234500, 1000000, "longExceeded",
        "Limite de exposição excedido — Dólar — total: posição comprada de US$ 1.234.500,00 acima do limite de US$ 1.000.000,00.")]
    [InlineData("usd", "Dólar — total", -1234500, 1000000, "shortExceeded",
        "Limite de exposição excedido — Dólar — total: posição vendida de US$ 1.234.500,00 acima do limite de US$ 1.000.000,00.")]
    public void BuildMessage_FourCases(string scope, string description, double value, double limit, string state, string expected)
    {
        var alert = new MarketLimitAlert(1, scope, description, (decimal)value, (decimal)limit, state);

        Assert.Equal(expected, MarketLimitAlertService.BuildMessage(alert));
    }

    private sealed class FailingSaveInterceptor(Func<Microsoft.EntityFrameworkCore.DbContext, bool> shouldFail) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (shouldFail(eventData.Context!))
                throw new DbUpdateException("Cannot insert duplicate key row in object 'MARKET_LIMIT_BREACHES' with unique index.");
            return ValueTask.FromResult(result);
        }
    }
}
