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

    /// <summary>
    /// Numa liberação COMUM quem consome é o Purchase(8); cancelar a perna de venda do
    /// par da Expedição não pode devolver saldo. O hook hoje dispara também para tipos de
    /// venda (precisa disparar para as liberações de transferência), então o que segura a
    /// regra é a fórmula por origem — e é isso que este teste protege.
    /// </summary>
    [Fact]
    public async Task Cancel_SalesShipmentLinkedToAStandardRelease_DoesNotTouchShippedQuantity()
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            ShippedQuantity = 80m,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.Standard,
        };

        StorageTransaction Leg(StorageTransactionType type) => new()
        {
            Key = Guid.NewGuid(),
            Code = "ST",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            NetWeight = 80m,
            ShipmentReleaseKey = release.Key,
        };

        // O par que a Expedição cria: é o Purchase(8) que responde pelos 80 consumidos.
        var purchase = Leg(StorageTransactionType.Purchase);
        var sales = Leg(StorageTransactionType.SalesShipment);

        _db.Context.ShipmentReleases.Add(release);
        _db.Context.StorageTransactions.AddRange(purchase, sales);
        await _db.Context.SaveChangesAsync();

        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var service = new StorageTransactionsCancelService(_db, recalc);

        await service.ExecuteAsync(sales.Key, "tester");

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(80m, reloaded.ShippedQuantity);
    }
}
