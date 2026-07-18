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

    private static StorageTransaction SalesTx(Guid releaseKey) =>
        Tx(releaseKey, StorageTransactionType.SalesShipment);

    private static StorageTransaction Tx(Guid? releaseKey, StorageTransactionType type) => new()
    {
        Key = Guid.NewGuid(), Code = "ST", CardCode = "F0001", ItemCode = "SOJA",
        UnitOfMeasureCode = "KG", WarehouseCode = "01",
        TransactionType = type,
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
    public async Task EnsureCanShip_NoReleaseKey_DoesNotThrow()
    {
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await service.EnsureCanShipAsync(Tx(null, StorageTransactionType.Purchase));
    }

    // Romaneio de compra contra liberação indisponível: sem o guard, um lançamento
    // novo poderia ir para o armazém da liberação já cancelada/trocada.

    [Theory]
    [InlineData(StorageTransactionType.Purchase, ReleaseStatus.Completed)]
    [InlineData(StorageTransactionType.Purchase, ReleaseStatus.Cancelled)]
    [InlineData(StorageTransactionType.Purchase, ReleaseStatus.Paused)]
    [InlineData(StorageTransactionType.PurchaseReturn, ReleaseStatus.Completed)]
    [InlineData(StorageTransactionType.PurchaseReturn, ReleaseStatus.Cancelled)]
    [InlineData(StorageTransactionType.PurchaseReturn, ReleaseStatus.Paused)]
    public async Task EnsureCanShip_PurchaseAgainstNonShippableRelease_Throws(
        StorageTransactionType type, ReleaseStatus status)
    {
        var key = await SeedReleaseAsync(status);
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await Assert.ThrowsAsync<ApplicationException>(() => service.EnsureCanShipAsync(Tx(key, type)));
    }

    [Theory]
    [InlineData(StorageTransactionType.Purchase)]
    [InlineData(StorageTransactionType.PurchaseReturn)]
    public async Task EnsureCanShip_PurchaseAgainstActivedRelease_DoesNotThrow(StorageTransactionType type)
    {
        var key = await SeedReleaseAsync(ReleaseStatus.Actived);
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await service.EnsureCanShipAsync(Tx(key, type)); // no throw
    }

    [Fact]
    public async Task EnsureCanShip_UnguardedType_DoesNotThrow()
    {
        var key = await SeedReleaseAsync(ReleaseStatus.Cancelled);
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await service.EnsureCanShipAsync(Tx(key, StorageTransactionType.Transfer)); // no throw
    }
}
