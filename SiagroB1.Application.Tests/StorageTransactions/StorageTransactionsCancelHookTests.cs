using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

public class StorageTransactionsCancelHookTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task Cancel_PurchaseLinkedToRelease_RecalculatesShippedQuantity()
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            ShippedQuantity = 80m,
            Status = ReleaseStatus.Actived,
        };
        var tx = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "ST",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            NetWeight = 80m,
            ShipmentReleaseKey = release.Key,
        };
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.StorageTransactions.Add(tx);
        await _db.Context.SaveChangesAsync();

        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var service = new StorageTransactionsCancelService(_db, recalc);

        await service.ExecuteAsync(tx.Key, "tester");

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(0m, reloaded.ShippedQuantity); // transação cancelada saiu da soma
    }

    [Fact]
    public async Task Cancel_SalesShipmentLinkedToRelease_DoesNotTouchShippedQuantity()
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            ShippedQuantity = 80m,
            Status = ReleaseStatus.Actived,
        };
        var tx = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "ST",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            NetWeight = 80m,
            ShipmentReleaseKey = release.Key,
        };
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.StorageTransactions.Add(tx);
        await _db.Context.SaveChangesAsync();

        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var service = new StorageTransactionsCancelService(_db, recalc);

        await service.ExecuteAsync(tx.Key, "tester");

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(80m, reloaded.ShippedQuantity); // hook não dispara para tipo de venda
    }
}
