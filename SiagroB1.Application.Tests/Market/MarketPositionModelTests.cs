using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Parte 4 (posição): entidades novas e campos de mercado nos contratos (opcionais, sem mudar o fluxo).</summary>
public class MarketPositionModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsCreateService Create() => new(
        _db.Context,
        new FakeBusinessPartnerService(names: new() { ["F0001"] = "PRODUTOR" }),
        new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeAgentService(new() { [1] = "AGENTE" }),
        new FakeWarehouseService(new() { ["01"] = "ARM 01" }),
        new FakeDocNumberSequenceService(),
        TestNotificationOutbox.For(_db.Context),
        new FakeStringLocalizer<Resource>(),
        NullLogger<PurchaseContractsCreateService>.Instance);

    private PurchaseContractsUpdateService Update() => new(
        _db.Context,
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR" }),
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeWarehouseService(new Dictionary<string, string> { ["01"] = "ARM 01" }),
        new FakeAgentService(new Dictionary<int, string> { [1] = "AGENTE" }),
        TestNotificationOutbox.For(_db.Context),
        NullLogger<PurchaseContractsUpdateService>.Instance);

    private static PurchaseContract NewContract() => new()
    {
        Key = Guid.NewGuid(), Code = "PC-MKT",
        CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01", AgentCode = 1, TotalVolume = 1_000m, StandardPrice = 2m,
        Type = ContractType.Fixed,
    };

    [Fact]
    public async Task PositionEntities_PersistAndReadBack()
    {
        var ctx = _db.Context;
        var detectedAt = new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc);

        ctx.MarketCommodities.Add(new MarketCommodity
        {
            Code = "SOY", Name = "Soja", HedgeExchange = MarketExchange.Cbot,
            Items = [new() { CommodityCode = "SOY", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" }],
            Months = [new() { CommodityCode = "SOY", DeliveryMonth = 6, FuturesMonth = 7, YearOffset = 0 }],
        });
        await ctx.SaveChangesAsync();

        var limit = new MarketExposureLimit
        {
            Scope = MarketLimitScope.Commodity, CommodityCode = "SOY", Year = 2027, Month = 7,
            MaxLong = 5_000m, MaxShort = 2_000.5m, UpdatedBy = "diretor", UpdatedAt = detectedAt,
        };
        ctx.MarketExposureLimits.Add(limit);
        await ctx.SaveChangesAsync();

        ctx.MarketLimitBreaches.Add(new MarketLimitBreach
        {
            LimitId = limit.Id, Date = new DateTime(2026, 11, 6), Value = 5_200m, LimitValue = 5_000m, DetectedAt = detectedAt,
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var commodity = await ctx.MarketCommodities.Include(c => c.Items).Include(c => c.Months).SingleAsync();
        Assert.Equal(MarketExchange.Cbot, commodity.HedgeExchange);
        Assert.True(commodity.Active);
        Assert.Equal("SOJA", Assert.Single(commodity.Items).ItemCode);
        Assert.Equal((6, 7, 0), (commodity.Months[0].DeliveryMonth, commodity.Months[0].FuturesMonth, commodity.Months[0].YearOffset));

        var savedLimit = await ctx.MarketExposureLimits.SingleAsync();
        Assert.Equal(MarketLimitScope.Commodity, savedLimit.Scope);
        Assert.Equal(2_000.5m, savedLimit.MaxShort);
        Assert.True(savedLimit.Active);
        Assert.Equal(DateTimeKind.Utc, savedLimit.UpdatedAt!.Value.Kind);

        var breach = await ctx.MarketLimitBreaches.SingleAsync();
        Assert.Equal(5_200m, breach.Value);
        Assert.Equal(detectedAt, breach.DetectedAt);
        Assert.Null(breach.NotifiedAt);
    }

    [Fact]
    public async Task PurchaseContractCreate_WithoutMarketFields_LeavesThemNull()
    {
        var contract = NewContract();
        await Create().ExecuteAsync(contract, "tester");

        _db.Context.ChangeTracker.Clear();
        var saved = await _db.Context.PurchaseContracts.SingleAsync(c => c.Key == contract.Key);
        Assert.Null(saved.MarketInstrumentCode);
        Assert.Null(saved.MarketExchangePrice);
        Assert.Null(saved.MarketPremium);
        Assert.Null(saved.MarketUsdPrice);
        Assert.Null(saved.MarketFxRate);
        Assert.False(saved.MarketFxLocked);
    }

    [Fact]
    public async Task PurchaseContract_MarketFields_SurviveCreateAndUpdate()
    {
        var contract = NewContract();
        contract.MarketInstrumentCode = "ZSN27";
        contract.MarketExchangePrice = 1180.25m;
        contract.MarketPremium = -35.5m;
        contract.MarketUsdPrice = 0.41234567m;
        contract.MarketFxRate = 5.4321m;
        contract.MarketFxLocked = true;
        await Create().ExecuteAsync(contract, "tester");

        _db.Context.ChangeTracker.Clear();
        var created = await _db.Context.PurchaseContracts.SingleAsync(c => c.Key == contract.Key);
        AssertFilled(created, 1180.25m);

        // Uma edição qualquer (volume) não mexe nos campos de mercado.
        created.TotalVolume = 900m;
        await Update().ExecuteAsync(contract.Key, created, "tester");

        _db.Context.ChangeTracker.Clear();
        var updated = await _db.Context.PurchaseContracts.SingleAsync(c => c.Key == contract.Key);
        Assert.Equal(900m, updated.TotalVolume);
        AssertFilled(updated, 1180.25m);

        // E editar os próprios campos de mercado grava.
        updated.MarketExchangePrice = 1200m;
        await Update().ExecuteAsync(contract.Key, updated, "tester");
        _db.Context.ChangeTracker.Clear();
        AssertFilled(await _db.Context.PurchaseContracts.SingleAsync(c => c.Key == contract.Key), 1200m);
    }

    private static void AssertFilled(PurchaseContract c, decimal exchangePrice)
    {
        Assert.Equal("ZSN27", c.MarketInstrumentCode);
        Assert.Equal(exchangePrice, c.MarketExchangePrice);
        Assert.Equal(-35.5m, c.MarketPremium);
        Assert.Equal(0.41234567m, c.MarketUsdPrice);
        Assert.Equal(5.4321m, c.MarketFxRate);
        Assert.True(c.MarketFxLocked);
    }
}
