using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

public class MarketPositionServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateOnly Today = new(2026, 11, 6);
    private const string Zsn7 = "ZSN7";
    private const string Spot = "USDSPOT";

    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));

    private static MarketInstrument Instrument(string code, MarketQuoteUnit unit, decimal size, decimal tonFactor,
        string commodity, DateTime? expiration, MarketInstrumentType type = MarketInstrumentType.Future) => new()
    {
        Code = code, Description = code, Commodity = commodity, Type = type, QuoteUnit = unit, ContractSize = size,
        TonFactor = tonFactor, ExpirationDate = expiration, Active = true,
    };

    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork(Guid.NewGuid().ToString());
        db.Context.MarketCommodities.Add(new MarketCommodity { Code = "SOY", Name = "Soja" });
        db.Context.MarketCommodityItems.Add(new MarketCommodityItem { CommodityCode = "SOY", ItemCode = "SOJA" });
        // Soja: entrega em junho → julho (ZSN).
        db.Context.MarketCommodityMonths.Add(new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = 6, FuturesMonth = 7 });
        db.Context.MarketCounterparties.Add(new MarketCounterparty
        {
            Id = 1, Name = "XP Corretora", Type = MarketCounterpartyType.Broker,
            Accounts = [new MarketCounterpartyAccount { Id = 1, CounterpartyId = 1, Code = "C-001" }],
        });
        db.Context.MarketInstruments.AddRange(
            Instrument(Zsn7, MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m, "SOY", new DateTime(2027, 7, 14)),
            Instrument("DOLZ6", MarketQuoteUnit.BrlPerUsd, 50000m, 0m, "USD", new DateTime(2026, 12, 1)),
            Instrument(Spot, MarketQuoteUnit.BrlPerUsd, 1m, 0m, "USD", null, MarketInstrumentType.Spot));
        db.Context.MarketTradingSettings.Add(new MarketTradingSettings { SpotFxInstrumentCode = Spot });
        await db.SaveChangesAsync();
        return db;
    }

    private static PurchaseContract Purchase(UnitOfWork db, string code, decimal kg, string item = "SOJA")
    {
        var c = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "F001", CardName = "PRODUTOR", ItemCode = item, BranchCode = "01",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "01", Type = ContractType.Fixed,
            Status = ContractStatus.Approved, TotalVolume = kg, StandardPrice = 2m, StandardCurrency = CurrencyType.Brl,
            DeliveryEndDate = new DateTime(2027, 6, 30),
        };
        db.Context.PurchaseContracts.Add(c);
        return c;
    }

    private static SalesContract Sales(UnitOfWork db, string code, decimal kg)
    {
        var c = new SalesContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "C001", CardName = "TRADING", ItemCode = "SOJA", BranchCode = "01",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", Type = ContractType.Fixed, Status = ContractStatus.Approved,
            TotalVolume = kg, Price = 2.2m, StandardCurrency = CurrencyType.Brl, DeliveryEndDate = new DateTime(2027, 6, 30),
        };
        db.Context.SalesContracts.Add(c);
        return c;
    }

    private static void Futures(UnitOfWork db, MarketTradeSide side, int lots, decimal price, string instrument = Zsn7) =>
        db.Context.MarketFuturesTrades.Add(new MarketFuturesTrade
        {
            TradedAt = new DateTime(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc), TradeDate = new DateTime(2026, 11, 5),
            AccountId = 1, InstrumentCode = instrument, Side = side, Lots = lots, OpenLots = lots, Price = price,
        });

    private static void Quote(UnitOfWork db, string instrument, decimal last) =>
        db.Context.MarketQuotes.Add(new MarketQuote { InstrumentCode = instrument, Last = last, QuotedAt = DateTime.UtcNow });

    private static MarketFxHedge Ndf(MarketTradeSide side, decimal usd, decimal rate, DateTime settlement,
        MarketFxHedgeStatus status = MarketFxHedgeStatus.Open, decimal? realized = null) => new()
    {
        TradedAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), TradeDate = new DateTime(2026, 11, 1), AccountId = 1,
        Kind = MarketFxHedgeKind.Ndf, Side = side, AmountUsd = usd, ContractedRate = rate, SettlementDate = settlement,
        Status = status, RealizedPnlBrl = realized,
    };

    private static MarketPositionService Service(UnitOfWork db, MarketExposureCache? cache = null)
    {
        var clock = Clock();
        var physical = new MarketPhysicalExposureService(db.Context, cache ?? new MarketExposureCache(clock),
            new FakeCompanyContext("0001"), clock);
        return new MarketPositionService(db.Context, physical, new MarketDerivativesPositionService(db.Context), clock);
    }

    private static async Task<MarketPositionSnapshot> Snapshot(UnitOfWork db)
    {
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return await Service(db).GetSnapshotAsync(Today, Ct);
    }

    private static MarketCommodityBucket SoyJuly(MarketPositionSnapshot s) =>
        s.Commodities.Single(c => c.Code == "SOY").Buckets.Single(b => b.Year == 2027 && b.Month == 7);

    // 4 lotes ZS = 4 × 5000 / 36,745 = 4 × 136,0729351 = 544,29174… → 544,2917 (a parte 3 arredonda uma vez, no fim);
    // o 544,2916 do plano era 4 × o lote já arredondado.
    [Fact]
    public async Task PurchaseAndShortFutures_NetCoveredAndHedgePercent()
    {
        var db = await SeedAsync();
        Purchase(db, "CC1", 1_000_000m);
        Futures(db, MarketTradeSide.Sell, 4, 1190m);

        var s = await Snapshot(db);

        var soy = s.Commodities.Single();
        Assert.Equal(("SOY", "Soja"), (soy.Code, soy.Name));
        var b = SoyJuly(s);
        Assert.Equal(Zsn7, b.InstrumentCode);
        Assert.Equal(1000m, b.PurchaseTons);
        Assert.Equal(0m, b.SalesTons);
        Assert.Equal(-544.2917m, b.FuturesTons);
        Assert.Equal(455.7083m, b.NetTons);
        Assert.Equal(544.2917m, b.CoveredTons);
        Assert.Equal(54.43m, b.HedgePercent);
        Assert.Equal(CurrencyType.Usd, b.Currency);
        Assert.Equal("none", b.LimitState);
        Assert.Equal(455.7083m, soy.Total.NetTons);
        Assert.Null(soy.Total.Year);
        Assert.Null(soy.Total.Month);
    }

    [Fact]
    public async Task PhysicalSale_ReducesPhysicalNet_HedgeRises()
    {
        var db = await SeedAsync();
        Purchase(db, "CC1", 1_000_000m);
        Sales(db, "CV1", 300_000m);
        Futures(db, MarketTradeSide.Sell, 4, 1190m);

        var b = SoyJuly(await Snapshot(db));

        Assert.Equal(1000m, b.PurchaseTons);
        Assert.Equal(300m, b.SalesTons);
        Assert.Equal(155.7083m, b.NetTons);
        Assert.Equal(544.2917m, b.CoveredTons);
        Assert.Equal(77.76m, b.HedgePercent);
    }

    [Fact]
    public async Task MissingQuote_TonsFilled_MarkToMarketNull()
    {
        var db = await SeedAsync();
        var c = Purchase(db, "CC1", 1_000_000m);
        c.MarketInstrumentCode = Zsn7;
        c.MarketExchangePrice = 1188.25m;
        Futures(db, MarketTradeSide.Sell, 4, 1190m);

        var s = await Snapshot(db);
        var b = SoyJuly(s);

        Assert.Equal(455.7083m, b.NetTons);
        Assert.Null(b.LastPrice);
        Assert.Null(b.PreviousClose);
        Assert.Null(b.FuturesUnrealized);
        Assert.Null(b.FuturesUnrealizedAtClose);
        Assert.Null(b.PhysicalExchangeMtm);
        Assert.Equal(0m, s.Usd.OpenCommodityValueUsd);
    }

    [Fact]
    public async Task FuturesUnrealized_MatchesPart3_AndAtCloseUsesLastDailyClose()
    {
        var db = await SeedAsync();
        Futures(db, MarketTradeSide.Sell, 4, 1190m);
        Quote(db, Zsn7, 1200m);
        db.Context.MarketDailyCloses.AddRange(
            new MarketDailyClose { InstrumentCode = Zsn7, Date = new DateTime(2026, 11, 4), Close = 1180m },
            new MarketDailyClose { InstrumentCode = Zsn7, Date = new DateTime(2026, 11, 5), Close = 1195m });
        await db.SaveChangesAsync();

        var part3 = (await new MarketDerivativesPositionService(db.Context).GetOpenPositionsAsync(Ct)).Single();
        var b = SoyJuly(await Snapshot(db));

        Assert.Equal(-2000m, part3.UnrealizedPnl); // (1200 − 1190) × −4 × 50
        Assert.Equal(part3.UnrealizedPnl, b.FuturesUnrealized);
        Assert.Equal(-1000m, b.FuturesUnrealizedAtClose); // (1195 − 1190) × −4 × 50
        Assert.Equal(1200m, b.LastPrice);
        Assert.Equal(1195m, b.PreviousClose);
    }

    [Fact]
    public async Task PhysicalExchangeMtm_ReferencedContracts_OthersCounted()
    {
        var db = await SeedAsync();
        var referenced = Purchase(db, "CC1", 1_000_000m);
        referenced.MarketInstrumentCode = Zsn7;
        referenced.MarketExchangePrice = 1188.25m;
        Purchase(db, "CC2", 200_000m);
        Sales(db, "CV1", 100_000m);
        Quote(db, Zsn7, 1200m);

        var s = await Snapshot(db);
        var b = SoyJuly(s);

        Assert.Equal(4317.54m, b.PhysicalExchangeMtm); // (1200 − 1188,25) × 0,36745 × 1000
        Assert.Equal(2, b.PhysicalWithoutReference);
        Assert.Null(b.FuturesUnrealized);
        // Informativo: 1.100 t líquidas × 1200 ¢/bu × 0,36745 = 485.034,00 US$.
        Assert.Equal(485034m, s.Usd.OpenCommodityValueUsd);
    }

    [Fact]
    public async Task Dollar_SalesReceivable_NdfSell_NetAndPercent()
    {
        var db = await SeedAsync();
        var sale = Sales(db, "CV1", 500_000m);
        sale.MarketUsdPrice = 0.4m;
        sale.StandardCashFlowDate = new DateTime(2026, 12, 15);
        var purchase = Purchase(db, "CC1", 100_000m);
        purchase.MarketUsdPrice = 0.35m;
        purchase.StandardCashFlowDate = new DateTime(2026, 12, 10);
        db.Context.MarketFxHedges.Add(Ndf(MarketTradeSide.Sell, 150_000m, 5.5m, new DateTime(2026, 12, 20)));
        Quote(db, Spot, 5.3m);

        var s = await Snapshot(db);

        Assert.Equal(5.3m, s.Usd.SpotFx);
        var b = s.Usd.Buckets.Single(x => x.Year == 2026 && x.Month == 12);
        Assert.Equal(200_000m, b.ReceivableUsd);
        Assert.Equal(35_000m, b.PayableUsd);
        Assert.Equal(-150_000m, b.FxHedgeUsd);
        Assert.Equal(0m, b.UsdFuturesUsd);
        Assert.Equal(15_000m, b.NetUsd);
        Assert.Equal(150_000m, b.CoveredUsd);
        Assert.Equal(90.91m, b.HedgePercent);
        Assert.Equal(30_000m, b.FxHedgeUnrealizedBrl); // (5,50 − 5,30) × 150.000
        Assert.Equal(15_000m, s.Usd.Total.NetUsd);
        Assert.Equal(30_000m, s.Results.UnrealizedBrl);
    }

    [Fact]
    public async Task Dollar_LockedContractOut_FuturesSigned()
    {
        var db = await SeedAsync();
        var locked = Sales(db, "CV1", 500_000m);
        locked.MarketUsdPrice = 0.4m;
        locked.MarketFxLocked = true;
        locked.StandardCashFlowDate = new DateTime(2026, 12, 15);
        Futures(db, MarketTradeSide.Sell, 2, 5.45m, "DOLZ6");

        var s = await Snapshot(db);

        var b = Assert.Single(s.Usd.Buckets);
        Assert.Equal((2026, 12), (b.Year, b.Month));
        Assert.Equal(0m, b.ReceivableUsd);
        Assert.Equal(-100_000m, b.UsdFuturesUsd);
        Assert.Equal(-100_000m, b.NetUsd);
        Assert.Null(b.HedgePercent);
        Assert.Empty(s.Commodities.SelectMany(c => c.Buckets).Where(x => x.FuturesTons != 0m));
    }

    [Fact]
    public async Task MissingSpot_DollarStillAnswers()
    {
        var db = await SeedAsync();
        db.Context.MarketFxHedges.Add(Ndf(MarketTradeSide.Sell, 150_000m, 5.5m, new DateTime(2026, 12, 20)));

        var s = await Snapshot(db);

        Assert.Null(s.Usd.SpotFx);
        var b = Assert.Single(s.Usd.Buckets);
        Assert.Equal(-150_000m, b.NetUsd);
        Assert.Null(b.FxHedgeUnrealizedBrl);
        Assert.Null(s.Results.UnrealizedBrl); // NDF aberto sem dólar à vista: sem base, não zero
    }

    [Fact]
    public async Task CommodityTotalLimitExceeded_AlertAndState_InactiveIgnored()
    {
        var db = await SeedAsync();
        Purchase(db, "CC1", 1_000_000m);
        Futures(db, MarketTradeSide.Sell, 4, 1190m);
        db.Context.MarketExposureLimits.AddRange(
            new MarketExposureLimit { Id = 7, Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", MaxLong = 400m, MaxShort = 100m },
            new MarketExposureLimit { Id = 8, Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", Year = 2027, Month = 7, MaxLong = 10m, Active = false });

        var s = await Snapshot(db);

        var soy = s.Commodities.Single();
        Assert.Equal("longExceeded", soy.Total.LimitState);
        Assert.Equal((400m, 100m), (soy.Total.MaxLong!.Value, soy.Total.MaxShort!.Value));
        Assert.Equal("none", SoyJuly(s).LimitState);
        var alert = Assert.Single(s.Alerts);
        Assert.Equal((7, "commodity", "Soja — total", 455.7083m, 400m, "longExceeded"),
            (alert.LimitId, alert.Scope, alert.Description, alert.Value, alert.LimitValue, alert.State));
    }

    [Fact]
    public async Task BucketAndUsdLimits_ShortExceeded()
    {
        var db = await SeedAsync();
        Sales(db, "CV1", 300_000m);
        db.Context.MarketFxHedges.Add(Ndf(MarketTradeSide.Sell, 150_000m, 5.5m, new DateTime(2026, 12, 20)));
        db.Context.MarketExposureLimits.AddRange(
            new MarketExposureLimit { Id = 1, Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", Year = 2027, Month = 7, MaxShort = 200m },
            new MarketExposureLimit { Id = 2, Scope = MarketLimitScope.Usd, Year = 2026, Month = 12, MaxShort = 100_000m });

        var s = await Snapshot(db);

        Assert.Equal("shortExceeded", SoyJuly(s).LimitState);
        Assert.Equal("none", s.Commodities.Single().Total.LimitState);
        Assert.Equal("shortExceeded", s.Usd.Buckets.Single().LimitState);
        Assert.Equal("none", s.Usd.Total.LimitState);
        Assert.Equal(2, s.Alerts.Count);
        var soy = s.Alerts.Single(a => a.LimitId == 1);
        Assert.Equal(("commodity", "Soja — 07/2027", -300m, 200m, "shortExceeded"),
            (soy.Scope, soy.Description, soy.Value, soy.LimitValue, soy.State));
        var usd = s.Alerts.Single(a => a.LimitId == 2);
        Assert.Equal(("usd", "Dólar — 12/2026", -150_000m, 100_000m), (usd.Scope, usd.Description, usd.Value, usd.LimitValue));
    }

    [Fact]
    public async Task WithinLimit_Ok()
    {
        var db = await SeedAsync();
        Purchase(db, "CC1", 100_000m);
        db.Context.MarketExposureLimits.Add(
            new MarketExposureLimit { Id = 1, Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", MaxLong = 400m });

        var s = await Snapshot(db);

        Assert.Equal("ok", s.Commodities.Single().Total.LimitState);
        Assert.Empty(s.Alerts);
    }

    [Fact]
    public async Task IgnoredContracts_PassedThrough()
    {
        var db = await SeedAsync();
        var c = Purchase(db, "CC9", 100_000m, item: "SORGO");

        var s = await Snapshot(db);

        var ignored = Assert.Single(s.Ignored);
        Assert.Equal((c.Key, MarketPhysicalExposureService.ReasonNoCommodity), (ignored.ContractKey, ignored.Reason));
        Assert.Empty(s.Commodities);
    }

    [Fact]
    public async Task FuturesWithoutExpiration_GoToNullBucket_UnknownCommodityNamedByCode()
    {
        var db = await SeedAsync();
        db.Context.MarketInstruments.Add(Instrument("ZCX", MarketQuoteUnit.UsdPerTon, 100m, 1m, "CORN", null));
        Futures(db, MarketTradeSide.Buy, 1, 200m, "ZCX");

        var s = await Snapshot(db);

        var corn = s.Commodities.Single(c => c.Code == "CORN");
        Assert.Equal("CORN", corn.Name);
        var b = Assert.Single(corn.Buckets);
        Assert.Null(b.Year);
        Assert.Null(b.Month);
        Assert.Equal(100m, b.FuturesTons);
        Assert.Null(b.HedgePercent);
    }

    [Fact]
    public async Task Results_CurrentYearOnly_ByCurrency()
    {
        var db = await SeedAsync();
        db.Context.MarketFuturesMatches.AddRange(
            new MarketFuturesMatch { InstrumentCode = Zsn7, AccountId = 1, Lots = 1, RealizedPnl = 3750m, Currency = CurrencyType.Usd, CloseDate = new DateTime(2026, 3, 2) },
            new MarketFuturesMatch { InstrumentCode = Zsn7, AccountId = 1, Lots = 1, RealizedPnl = 999m, Currency = CurrencyType.Usd, CloseDate = new DateTime(2025, 12, 30) },
            new MarketFuturesMatch { InstrumentCode = "CCMK7", AccountId = 1, Lots = 1, RealizedPnl = -500m, Currency = CurrencyType.Brl, CloseDate = new DateTime(2026, 5, 2) });
        db.Context.MarketFxHedges.AddRange(
            Ndf(MarketTradeSide.Sell, 10_000m, 5.5m, new DateTime(2026, 9, 1), MarketFxHedgeStatus.Settled, 1200m),
            Ndf(MarketTradeSide.Sell, 10_000m, 5.5m, new DateTime(2025, 9, 1), MarketFxHedgeStatus.Settled, 7777m));
        Futures(db, MarketTradeSide.Sell, 4, 1190m);
        Quote(db, Zsn7, 1200m);

        var s = await Snapshot(db);

        Assert.Equal(3750m, s.Results.RealizedUsd);
        Assert.Equal(700m, s.Results.RealizedBrl);
        Assert.Equal(-2000m, s.Results.UnrealizedUsd);
        Assert.Equal(0m, s.Results.UnrealizedBrl);
    }

    [Fact]
    public async Task BucketContracts_AndUsdContracts_ListRows()
    {
        var db = await SeedAsync();
        var c = Purchase(db, "CC1", 1_000_000m);
        c.MarketInstrumentCode = Zsn7;
        c.MarketExchangePrice = 1188.25m;
        var locked = Sales(db, "CV1", 500_000m);
        locked.MarketUsdPrice = 0.4m;
        locked.MarketFxLocked = true;
        locked.MarketFxRate = 5.4m;
        locked.StandardCashFlowDate = new DateTime(2026, 12, 15);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await Service(db).GetBucketContractsAsync("soy", 2027, 7, Ct);
        Assert.Equal(2, rows.Count);
        var p = rows.Single(r => r.Side == MarketEfpContractSide.Purchase);
        Assert.Equal(("CC1", 1000m, 120m, Zsn7, 1188.25m, false), (p.ContractCode, p.FirmOpenTons, p.PricePerBag, p.MarketInstrumentCode, p.MarketExchangePrice, p.UsdLinked));

        var usd = Assert.Single(await Service(db).GetUsdContractsAsync(Ct));
        Assert.Equal(("CV1", 500m, 200_000m, 24m, true, 5.4m, new DateOnly(2026, 12, 15)),
            (usd.ContractCode, usd.FirmOpenTons, usd.UsdAmount, usd.UsdPricePerBag, usd.FxLocked, usd.FxRate, usd.CashFlowDate));
    }

    [Fact]
    public async Task FxLock_InvalidatesExposureCache()
    {
        var db = await SeedAsync();
        var sale = Sales(db, "CV1", 500_000m);
        sale.MarketUsdPrice = 0.4m;
        await db.SaveChangesAsync();
        var clock = Clock();
        var cache = new MarketExposureCache(clock);
        cache.Set("0001", new object());

        var fxLock = new MarketContractFxLockService(db.Context,
            new MarketPermissionGuard(new FakeUserPermissions([PermissionCodes.MarketManage])),
            new PurchaseContractsChangeLogService(db.Context), new SalesContractsChangeLogService(db.Context),
            cache, new FakeCompanyContext("0001"));
        await fxLock.SetAsync(MarketEfpContractSide.Sales, sale.Key, true, null, "u", Ct);

        Assert.False(cache.TryGet<object>("0001", out _));
    }

    [Fact]
    public async Task ControllerJson_ContractShape()
    {
        var db = await SeedAsync();
        Purchase(db, "CC9", 100_000m, item: "SORGO");
        Sales(db, "CV1", 300_000m);
        db.Context.MarketExposureLimits.Add(
            new MarketExposureLimit { Id = 1, Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", MaxShort = 10m });
        var s = await Snapshot(db);

        var view = SiagroB1.Web.Controllers.MarketPositionController.WithConnection(s,
            new LiveQuoteSnapshot(3, CmaConnectionStatus.Connected, "Ao vivo", null));
        var json = System.Text.Json.JsonSerializer.Serialize(view, SiagroB1.Web.Market.MarketQuotesStreamLoop.JsonOptions);

        Assert.Contains("\"generatedAt\":\"2026-11-06T15:00:00Z\"", json);
        Assert.Contains("\"connectionStatus\":\"connected\"", json);
        Assert.Contains("\"side\":\"purchase\"", json);
        Assert.Contains("\"currency\":\"usd\"", json);
        Assert.Contains("\"limitState\":\"shortExceeded\"", json);
        Assert.Contains("\"scope\":\"commodity\"", json);
        Assert.Contains("\"physicalWithoutReference\":1", json);

        var rows = await Service(db).GetBucketContractsAsync("SOY", 2027, 7, Ct);
        var rowsJson = System.Text.Json.JsonSerializer.Serialize(rows, SiagroB1.Web.Market.MarketQuotesStreamLoop.JsonOptions);
        Assert.Contains("\"deliveryEndDate\":\"2027-06-30\"", rowsJson);
        Assert.Contains("\"side\":\"sales\"", rowsJson);
    }

    [Fact]
    public async Task Unrealized_UsdFuturesWithoutQuote_Null_NoPosition_Zero()
    {
        var db = await SeedAsync();
        Futures(db, MarketTradeSide.Sell, 4, 1190m);
        var s = await Snapshot(db);
        Assert.Null(s.Results.UnrealizedUsd);
        Assert.Equal(0m, s.Results.UnrealizedBrl);

        var empty = await Snapshot(await SeedAsync());
        Assert.Equal((0m, 0m), (empty.Results.UnrealizedUsd!.Value, empty.Results.UnrealizedBrl!.Value));
    }

    [Fact]
    public async Task Dollar_ContractWithoutCommodity_CountsInUsd_AndListed_LockedOnlyListed()
    {
        var db = await SeedAsync();
        var sale = Sales(db, "CV7", 500_000m);
        sale.ItemCode = "SORGO"; // sem commodity: fora das toneladas
        sale.MarketUsdPrice = 0.4m;
        sale.StandardCashFlowDate = new DateTime(2026, 12, 15);
        var purchase = Purchase(db, "CC7", 100_000m, item: "SORGO");
        purchase.StandardCurrency = CurrencyType.Usd;
        purchase.StandardPrice = 0.3m;
        purchase.StandardCashFlowDate = new DateTime(2026, 12, 15);
        Purchase(db, "CC8", 100_000m, item: "SORGO"); // R$ sem MarketUsdPrice: não é dólar

        var s = await Snapshot(db);

        Assert.Equal(3, s.Ignored.Count);
        var b = Assert.Single(s.Usd.Buckets);
        Assert.Equal((200_000m, 30_000m, 170_000m), (b.ReceivableUsd, b.PayableUsd, b.NetUsd));
        var listed = await Service(db).GetUsdContractsAsync(Ct);
        Assert.Equal(["CC7", "CV7"], listed.Select(r => r.ContractCode).Order());

        db.Context.SalesContracts.Attach(sale);
        sale.MarketFxLocked = true;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var locked = await Service(db).GetSnapshotAsync(Today, Ct);
        Assert.Equal((0m, -30_000m), (locked.Usd.Total.ReceivableUsd, locked.Usd.Total.NetUsd));
        Assert.Contains(await Service(db).GetUsdContractsAsync(Ct), r => r.ContractCode == "CV7" && r.FxLocked);
    }
}
