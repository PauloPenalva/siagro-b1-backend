using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketPricingTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketFreightRateServiceTests
{
    private static MarketFreightRate Rate(DateTime validFrom, decimal value = 100m, int destination = 1) => new()
    {
        MunicipalityCode = "4104808", DestinationId = destination, RatePerTon = value, ValidFrom = validFrom,
    };

    [Fact]
    public async Task GetEffectiveRates_PicksLatestNotInFuture()
    {
        var db = await SeedPricingAsync();
        var service = FreightService(db);
        await service.CreateAsync(Rate(new DateTime(2026, 10, 1), 100m), "ana");
        await service.CreateAsync(Rate(new DateTime(2026, 11, 1), 120m), "ana");
        await service.CreateAsync(Rate(new DateTime(2026, 12, 1), 150m), "ana");

        var rates = await service.GetEffectiveRatesAsync("4104808", new DateOnly(2026, 11, 6), CancellationToken.None);

        Assert.Equal(120m, Assert.Single(rates).Value);
    }

    [Fact]
    public async Task GetEffectiveRates_NoneYet_OmitsDestination()
    {
        var db = await SeedPricingAsync();
        var service = FreightService(db);
        await service.CreateAsync(Rate(new DateTime(2026, 12, 1)), "ana");

        var rates = await service.GetEffectiveRatesAsync("4104808", new DateOnly(2026, 11, 6), CancellationToken.None);

        Assert.Empty(rates);
    }

    [Fact]
    public async Task FreightRate_DuplicateKey_BusinessException()
    {
        var db = await SeedPricingAsync();
        var service = FreightService(db);
        await service.CreateAsync(Rate(new DateTime(2026, 11, 1)), "ana");

        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Rate(new DateTime(2026, 11, 1), 90m), "ana"));
    }

    [Fact]
    public async Task FreightRate_UnknownMunicipalityOrNegativeRate_Rejected()
    {
        var db = await SeedPricingAsync();
        var service = FreightService(db);
        var unknown = Rate(new DateTime(2026, 11, 1));
        unknown.MunicipalityCode = "0000000";

        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(unknown, "ana"));
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Rate(new DateTime(2026, 11, 1), -1m), "ana"));
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Rate(new DateTime(2026, 11, 1), 1m, destination: 99), "ana"));
    }

    [Fact]
    public async Task FreightRate_Create_StampsAuditAndKeepsItOnUpdate()
    {
        var db = await SeedPricingAsync();
        var service = FreightService(db);
        var created = await service.CreateAsync(Rate(new DateTime(2026, 11, 1)), "ana");
        var createdAt = created.CreatedAt;
        db.Context.ChangeTracker.Clear();

        await service.UpdateAsync(created.Id, Rate(new DateTime(2026, 11, 1), 130m), "bia");

        var stored = await db.Context.MarketFreightRates.AsNoTracking().SingleAsync();
        Assert.Equal("ana", stored.CreatedBy);
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal(130m, stored.RatePerTon);
        Assert.NotEqual(default, createdAt);
    }

    [Fact]
    public async Task FreightRate_Write_BumpsLiveVersion()
    {
        var db = await SeedPricingAsync();
        var store = new LiveQuoteStore();
        var before = store.Get("0001").Version;

        await FreightService(db, store).CreateAsync(Rate(new DateTime(2026, 11, 1)), "ana");

        Assert.True(store.Get("0001").Version > before);
    }

    [Fact]
    public async Task GetCities_OnlyWithFreight_OrderedByName()
    {
        var db = await SeedPricingAsync();
        db.Context.Municipalities.Add(new SiagroB1.Domain.Entities.Municipality { Code = "4100000", Name = "Sem frete", StateAbbreviation = "PR" });
        await db.SaveChangesAsync();
        var service = FreightService(db);
        await service.CreateAsync(Rate(new DateTime(2026, 11, 1)), "ana");

        var cities = await service.GetCitiesAsync(CancellationToken.None);

        var city = Assert.Single(cities);
        Assert.Equal(new MarketFreightCityShape("4104808", "Cascavel", "PR"),
            new MarketFreightCityShape(city.Code, city.Name, city.StateAbbreviation));
    }

    private sealed record MarketFreightCityShape(string Code, string Name, string Uf);

    [Fact]
    public async Task Destination_DeleteInUse_Rejected()
    {
        var db = await SeedPricingAsync();
        await FreightService(db).CreateAsync(Rate(new DateTime(2026, 11, 1)), "ana");

        await Assert.ThrowsAsync<BusinessException>(() => DestinationService(db).DeleteAsync(1));
    }

    [Fact]
    public async Task Destination_DuplicateName_Rejected()
    {
        var db = await SeedPricingAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            DestinationService(db).CreateAsync(new MarketDestination { Name = "  paranaguá " }));
    }
}
