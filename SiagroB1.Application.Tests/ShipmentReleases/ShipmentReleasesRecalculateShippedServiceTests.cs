using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesRecalculateShippedServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesRecalculateShippedService Service() => new(_db.Context);

    private async Task<ShipmentRelease> SeedReleaseAsync(decimal released)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = released,
            ShippedQuantity = 999m, // valor errado, deve ser recalculado
            Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private StorageTransaction Tx(Guid releaseKey, StorageTransactionType type, decimal net,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        Code = "ST",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TransactionType = type,
        TransactionStatus = status,
        NetWeight = net,
        ShipmentReleaseKey = releaseKey,
    };

    private async Task<decimal> ShippedAsync(Guid key) =>
        (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key)).ShippedQuantity;

    [Fact]
    public async Task Recalc_ShipmentMinusReturn_UsingNetWeight()
    {
        var sr = await SeedReleaseAsync(released: 100m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 80m),
            Tx(sr.Key, StorageTransactionType.SalesShipmentReturn, 30m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(50m, await ShippedAsync(sr.Key)); // 80 − 30
    }

    [Fact]
    public async Task Recalc_IgnoresCancelled_CountsPending()
    {
        var sr = await SeedReleaseAsync(released: 100m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 40m, StorageTransactionsStatus.Pending),
            Tx(sr.Key, StorageTransactionType.SalesShipment, 25m, StorageTransactionsStatus.Cancelled));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(40m, await ShippedAsync(sr.Key)); // pending conta, cancelled não
    }

    [Fact]
    public async Task Recalc_IgnoresOtherTypesAndOtherReleases()
    {
        var sr = await SeedReleaseAsync(released: 100m);
        var other = Guid.NewGuid();
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 500m),          // tipo ignorado
            Tx(other, StorageTransactionType.SalesShipment, 70m),       // outro release
            Tx(sr.Key, StorageTransactionType.SalesShipment, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_NoTransactions_SetsZero()
    {
        var sr = await SeedReleaseAsync(released: 100m);

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }
}
