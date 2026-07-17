using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleaseMovementGuardServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<Guid> SeedReleaseAsync(ReleaseStatus status)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01", ReleasedQuantity = 100m, Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr.Key;
    }

    private static StorageTransaction SalesTx(Guid releaseKey) => new()
    {
        Key = Guid.NewGuid(), Code = "ST", CardCode = "F0001", ItemCode = "SOJA",
        UnitOfMeasureCode = "KG", WarehouseCode = "01",
        TransactionType = StorageTransactionType.SalesShipment,
        ShipmentReleaseKey = releaseKey,
    };

    [Theory]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Paused)]
    public async Task EnsureCanShip_NonShippableRelease_Throws(ReleaseStatus status)
    {
        var key = await SeedReleaseAsync(status);
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await Assert.ThrowsAsync<ApplicationException>(() => service.EnsureCanShipAsync(SalesTx(key)));
    }

    [Fact]
    public async Task EnsureCanShip_ActivedRelease_DoesNotThrow()
    {
        var key = await SeedReleaseAsync(ReleaseStatus.Actived);
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await service.EnsureCanShipAsync(SalesTx(key)); // no throw
    }

    [Fact]
    public async Task EnsureCanShip_NoReleaseKeyOrNonSalesType_DoesNotThrow()
    {
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await service.EnsureCanShipAsync(new StorageTransaction
        {
            Key = Guid.NewGuid(), CardCode = "F", ItemCode = "S", UnitOfMeasureCode = "KG", WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase, ShipmentReleaseKey = null,
        });
    }
}
