using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesGetAvailableServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesShipmentReleasesGetAvailableService Service() => new(_db);

    private async Task SeedAsync(
        string itemCode, ReleaseStatus status, decimal released, decimal shipped, decimal price = 50m)
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC", CardCode = "C0001", CardName = "Cliente",
            ItemCode = itemCode, ItemName = itemCode, UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", TotalVolume = 10000m, Price = price,
        };
        _db.Context.SalesContracts.Add(sc);
        _db.Context.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = sc.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = released, ShippedQuantity = shipped, Status = status,
        });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Query_ReturnsOnlyActivedWithBalanceForItem()
    {
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 200m);   // ok (saldo 800)
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 500m, shipped: 500m);    // sem saldo
        await SeedAsync("SOJA", ReleaseStatus.Pending, released: 1000m, shipped: 0m);     // não ativa
        await SeedAsync("MILHO", ReleaseStatus.Actived, released: 1000m, shipped: 0m);    // outro produto

        var result = Service().Query("SOJA").ToList();

        Assert.Single(result);
        Assert.Equal(800m, result[0].AvailableQuantity);
        Assert.Equal("SOJA", result[0].ItemCode);
        Assert.Equal(50m, result[0].Price);
        Assert.Equal("C0001", result[0].CardCode);
    }
}
