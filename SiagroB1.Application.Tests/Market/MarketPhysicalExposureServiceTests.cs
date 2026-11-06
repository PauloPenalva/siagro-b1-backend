using SiagroB1.Application.Services.Market.Position;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

public class MarketPhysicalExposureServiceTests
{
    private static readonly DateTime June2027 = new(2027, 6, 30);

    private static ManualTimeProvider NewClock() => new(new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.Zero));

    private static UnitOfWork NewDb()
    {
        var db = TestDb.CreateUnitOfWork(Guid.NewGuid().ToString());
        db.Context.MarketCommodities.AddRange(
            new MarketCommodity { Code = "SOY", Name = "Soja" },
            new MarketCommodity { Code = "OLD", Name = "Inativa", Active = false });
        db.Context.MarketCommodityItems.AddRange(
            new MarketCommodityItem { CommodityCode = "SOY", ItemCode = "SOJA" },
            new MarketCommodityItem { CommodityCode = "OLD", ItemCode = "SORGO" });
        // Soja: junho → julho (ZSN); sem regra para agosto.
        db.Context.MarketCommodityMonths.AddRange(
            new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = 6, FuturesMonth = 7, YearOffset = 0 },
            new MarketCommodityMonth { CommodityCode = "SOY", DeliveryMonth = 12, FuturesMonth = 1, YearOffset = 1 },
            new MarketCommodityMonth { CommodityCode = "OLD", DeliveryMonth = 6, FuturesMonth = 7, YearOffset = 0 });
        return db;
    }

    private static PurchaseContract Purchase(string code, ContractType type = ContractType.Fixed,
        ContractStatus? status = ContractStatus.Approved, decimal total = 600_000m, string item = "SOJA",
        string uom = "KG", DateTime? deliveryEnd = null, CurrencyType currency = CurrencyType.Brl) => new()
    {
        Key = Guid.NewGuid(), Code = code, CardCode = "F001", CardName = "PRODUTOR", ItemCode = item, BranchCode = "01",
        UnitOfMeasureCode = uom, HarvestSeasonCode = "2026", DeliveryLocationCode = "01", Type = type, Status = status,
        TotalVolume = total, StandardPrice = type == ContractType.Fixed ? 2m : 0m, StandardCurrency = currency,
        DeliveryEndDate = deliveryEnd ?? June2027,
    };

    private static PurchaseContractPriceFixation Fix(PurchaseContract c, decimal volume, decimal price,
        PriceFixationStatus status = PriceFixationStatus.Confirmed) => new()
    {
        PurchaseContractKey = c.Key, FixationVolume = volume, FixationPrice = price, Status = status,
    };

    private static PurchaseContract AddFixed(UnitOfWork db, PurchaseContract c)
    {
        db.Context.PurchaseContracts.Add(c);
        db.Context.PurchaseContractsPriceFixations.Add(Fix(c, c.TotalVolume, c.StandardPrice));
        return c;
    }

    private static MarketPhysicalExposureService Service(UnitOfWork db, MarketExposureCache? cache = null,
        TimeProvider? clock = null, string company = "0001")
    {
        var time = clock ?? NewClock();
        return new MarketPhysicalExposureService(db.Context, cache ?? new MarketExposureCache(time),
            new FakeCompanyContext(company), time);
    }

    private static async Task<MarketPhysicalExposure> Read(UnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        return await Service(db).ReadAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FixedApprovedPurchase_NothingDelivered_RowInSoyJuly()
    {
        var db = NewDb();
        var c = AddFixed(db, Purchase("CC1"));

        var result = await Read(db);

        var row = Assert.Single(result.Rows);
        Assert.Equal((MarketEfpContractSide.Purchase, c.Key, "SOY", 2027, 7, 600_000m, 2m),
            (row.Side, row.ContractKey, row.CommodityCode, row.FuturesYear, row.FuturesMonth, row.FirmOpenKg, row.PricePerKg));
        Assert.Equal(new DateOnly(2027, 6, 30), row.DeliveryEndDate);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public async Task AllocatedVolume_ReducesFirmOpen()
    {
        var db = NewDb();
        var c = Purchase("CC1");
        c.AllocatedVolume = 200_000m;
        AddFixed(db, c);

        Assert.Equal(400_000m, Assert.Single((await Read(db)).Rows).FirmOpenKg);
    }

    [Fact]
    public async Task PafPartialFixation_OnlyFixedNotDelivered()
    {
        var db = NewDb();
        var c = Purchase("CC1", ContractType.ToBeDetermined, total: 1_000_000m);
        c.AllocatedVolume = 100_000m;
        db.Context.PurchaseContracts.Add(c);
        db.Context.PurchaseContractsPriceFixations.Add(Fix(c, 300_000m, 2m));

        Assert.Equal(200_000m, Assert.Single((await Read(db)).Rows).FirmOpenKg);
    }

    [Fact]
    public async Task FixationInApproval_DoesNotCount()
    {
        var db = NewDb();
        var c = Purchase("CC1", ContractType.ToBeDetermined);
        db.Context.PurchaseContracts.Add(c);
        db.Context.PurchaseContractsPriceFixations.Add(Fix(c, 300_000m, 2m, PriceFixationStatus.InApproval));

        var result = await Read(db);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public async Task WeightedAverage_TwoFixations()
    {
        var db = NewDb();
        var c = Purchase("CC1", ContractType.ToBeDetermined, total: 1_000_000m);
        db.Context.PurchaseContracts.Add(c);
        db.Context.PurchaseContractsPriceFixations.AddRange(Fix(c, 100_000m, 2m), Fix(c, 300_000m, 2.4m));

        var row = Assert.Single((await Read(db)).Rows);
        Assert.Equal(400_000m, row.FirmOpenKg);
        Assert.Equal(2.3m, row.PricePerKg);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    [InlineData(ContractStatus.InApproval)]
    [InlineData(null)]
    public async Task NotApproved_Excluded(ContractStatus? status)
    {
        var db = NewDb();
        AddFixed(db, Purchase("CC1", status: status));

        var result = await Read(db);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public async Task SalesContract_RowWithSalesSide()
    {
        var db = NewDb();
        var s = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "CV1", CardCode = "C001", CardName = "CLIENTE", ItemCode = "SOJA",
            BranchCode = "01", UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", TotalVolume = 500_000m,
            AllocatedVolume = 100_000m, Type = ContractType.Fixed, Status = ContractStatus.Approved, Price = 2.5m,
            DeliveryEndDate = new DateTime(2027, 12, 10),
        };
        db.Context.SalesContracts.Add(s);
        db.Context.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
        {
            SalesContractKey = s.Key, FixationVolume = 500_000m, FixationPrice = 2.5m, Status = PriceFixationStatus.Confirmed,
        });

        var row = Assert.Single((await Read(db)).Rows);
        Assert.Equal((MarketEfpContractSide.Sales, "CV1", "CLIENTE", 2028, 1, 400_000m, 2.5m),
            (row.Side, row.ContractCode, row.CardName, row.FuturesYear, row.FuturesMonth, row.FirmOpenKg, row.PricePerKg));
    }

    [Fact]
    public async Task ProductWithoutCommodity_Ignored()
    {
        var db = NewDb();
        var c = AddFixed(db, Purchase("CC1", item: "MILHO"));

        var result = await Read(db);
        Assert.Empty(result.Rows);
        var ignored = Assert.Single(result.Ignored);
        Assert.Equal((c.Key, "CC1", "MILHO", "Produto sem commodity configurada"),
            (ignored.ContractKey, ignored.ContractCode, ignored.ItemCode, ignored.Reason));
    }

    [Fact]
    public async Task InactiveCommodity_Ignored()
    {
        var db = NewDb();
        AddFixed(db, Purchase("CC1", item: "SORGO"));

        Assert.Equal("Produto sem commodity configurada", Assert.Single((await Read(db)).Ignored).Reason);
    }

    [Fact]
    public async Task ProductWithoutCommodity_NothingFirmOpen_NotListed()
    {
        var db = NewDb();
        db.Context.PurchaseContracts.Add(Purchase("CC1", ContractType.ToBeDetermined, item: "MILHO"));

        var result = await Read(db);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public async Task NotKg_Ignored()
    {
        var db = NewDb();
        AddFixed(db, Purchase("CC1", uom: "SC"));

        Assert.Equal("Contrato em unidade diferente de KG", Assert.Single((await Read(db)).Ignored).Reason);
    }

    [Fact]
    public async Task MonthWithoutRule_Ignored()
    {
        var db = NewDb();
        AddFixed(db, Purchase("CC1", deliveryEnd: new DateTime(2027, 8, 31)));

        Assert.Equal("Mês de entrega sem vencimento de bolsa configurado", Assert.Single((await Read(db)).Ignored).Reason);
    }

    [Fact]
    public async Task NoDeliveryEndDate_Ignored()
    {
        var db = NewDb();
        var c = Purchase("CC1");
        c.DeliveryEndDate = default;
        AddFixed(db, c);

        Assert.Equal("Contrato sem data final de entrega", Assert.Single((await Read(db)).Ignored).Reason);
    }

    [Fact]
    public async Task FullyDelivered_NeitherRowNorIgnored()
    {
        var db = NewDb();
        var c = Purchase("CC1", item: "MILHO");
        c.AllocatedVolume = 600_000m;
        AddFixed(db, c);

        var result = await Read(db);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public async Task UsdContract_UsdLinkedWithFixationPrice()
    {
        var db = NewDb();
        var c = Purchase("CC1", ContractType.ToBeDetermined, currency: CurrencyType.Usd);
        db.Context.PurchaseContracts.Add(c);
        db.Context.PurchaseContractsPriceFixations.Add(Fix(c, 100_000m, 0.4m));

        var row = Assert.Single((await Read(db)).Rows);
        Assert.True(row.UsdLinked);
        Assert.Equal(CurrencyType.Usd, row.Currency);
        Assert.Equal(0.4m, row.UsdPricePerKg);
    }

    [Fact]
    public async Task BrlContractWithMarketUsdPrice_UsdLinked()
    {
        var db = NewDb();
        var c = Purchase("CC1");
        c.MarketUsdPrice = 0.38m;
        c.MarketFxLocked = true;
        AddFixed(db, c);

        var row = Assert.Single((await Read(db)).Rows);
        Assert.True(row.UsdLinked);
        Assert.True(row.FxLocked);
        Assert.Equal(0.38m, row.UsdPricePerKg);
    }

    [Fact]
    public async Task BrlContract_NotUsdLinked()
    {
        var db = NewDb();
        AddFixed(db, Purchase("CC1"));

        var row = Assert.Single((await Read(db)).Rows);
        Assert.False(row.UsdLinked);
        Assert.Null(row.UsdPricePerKg);
    }

    [Fact]
    public async Task CashFlowDate_FallsBackToDeliveryEnd_OrUsesStandardCashFlow()
    {
        var db = NewDb();
        var withCash = Purchase("CC1");
        withCash.StandardCashFlowDate = new DateTime(2027, 7, 15);
        AddFixed(db, withCash);
        AddFixed(db, Purchase("CC2"));

        var rows = (await Read(db)).Rows;
        Assert.Equal(new DateOnly(2027, 7, 15), rows.Single(r => r.ContractCode == "CC1").CashFlowDate);
        Assert.Equal(new DateOnly(2027, 6, 30), rows.Single(r => r.ContractCode == "CC2").CashFlowDate);
    }

    [Fact]
    public async Task FixedContractWithoutFixationRow_WholeVolumeAtContractPrice()
    {
        var db = NewDb();
        db.Context.PurchaseContracts.Add(Purchase("CC1"));

        var row = Assert.Single((await Read(db)).Rows);
        Assert.Equal((600_000m, 2m), (row.FirmOpenKg, row.PricePerKg));
    }

    [Fact]
    public async Task GetAsync_CachedWithinValidity_RereadsAfterInvalidateOrExpiry()
    {
        var db = NewDb();
        AddFixed(db, Purchase("CC1"));
        await db.Context.SaveChangesAsync();
        var clock = NewClock();
        var cache = new MarketExposureCache(clock);
        var service = Service(db, cache, clock);

        var first = await service.GetAsync(CancellationToken.None);
        AddFixed(db, Purchase("CC2"));
        await db.Context.SaveChangesAsync();

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Same(first, await service.GetAsync(CancellationToken.None));

        cache.Invalidate("0001");
        var reread = await service.GetAsync(CancellationToken.None);
        Assert.Equal(2, reread.Rows.Count);

        AddFixed(db, Purchase("CC3"));
        await db.Context.SaveChangesAsync();
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(3, (await service.GetAsync(CancellationToken.None)).Rows.Count);
    }

    [Fact]
    public async Task GetAsync_CacheIsPerCompany()
    {
        var db = NewDb();
        AddFixed(db, Purchase("CC1"));
        await db.Context.SaveChangesAsync();
        var clock = NewClock();
        var cache = new MarketExposureCache(clock);

        var a = await Service(db, cache, clock, "0001").GetAsync(CancellationToken.None);
        var b = await Service(db, cache, clock, "0002").GetAsync(CancellationToken.None);

        Assert.NotSame(a, b);
        cache.Invalidate("0001");
        Assert.Same(b, await Service(db, cache, clock, "0002").GetAsync(CancellationToken.None));
    }
}
