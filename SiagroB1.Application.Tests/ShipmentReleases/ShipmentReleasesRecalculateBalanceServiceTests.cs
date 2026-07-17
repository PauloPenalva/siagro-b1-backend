using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesRecalculateBalanceServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesRecalculateBalanceService Service() =>
        new(_db.Context, new ShipmentReleasesRecalculateShippedService(_db.Context));

    private async Task<ShipmentRelease> SeedReleaseAsync(decimal released, decimal shipped, ReleaseStatus status = ReleaseStatus.Actived)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = released,
            ShippedQuantity = shipped,
            Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private StorageTransaction Tx(Guid releaseKey, decimal net) => new()
    {
        Key = Guid.NewGuid(), Code = "ST", CardCode = "F0001", ItemCode = "SOJA",
        UnitOfMeasureCode = "KG", WarehouseCode = "01",
        TransactionType = StorageTransactionType.SalesShipment,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        NetWeight = net, ShipmentReleaseKey = releaseKey,
    };

    [Fact]
    public async Task ExecuteAsync_CorrectsDivergentShipped_ReportsBeforeAfter()
    {
        var sr = await SeedReleaseAsync(released: 100m, shipped: 999m); // errado
        _db.Context.StorageTransactions.Add(Tx(sr.Key, 30m));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(sr.Key);

        Assert.True(result.Changed);
        Assert.Equal(999m, result.PreviousShippedQuantity);
        Assert.Equal(30m, result.NewShippedQuantity);
        Assert.Equal(70m, result.NewAvailableQuantity); // 100 − 30
    }

    [Fact]
    public async Task ExecuteAsync_CompletedRelease_Throws()
    {
        var sr = await SeedReleaseAsync(released: 100m, shipped: 30m, status: ReleaseStatus.Completed);

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(sr.Key));
    }

    [Fact]
    public async Task ExecuteAsync_NotFound_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service().ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExecuteAllAsync_ExcludesCompleted_ListsChanged()
    {
        var ok = await SeedReleaseAsync(released: 100m, shipped: 0m);
        _db.Context.StorageTransactions.Add(Tx(ok.Key, 0m)); // shipped 0 → sem mudança
        var wrong = await SeedReleaseAsync(released: 100m, shipped: 0m);
        _db.Context.StorageTransactions.Add(Tx(wrong.Key, 40m)); // divergente
        var completed = await SeedReleaseAsync(released: 100m, shipped: 777m, status: ReleaseStatus.Completed);
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAllAsync();

        Assert.Equal(2, result.Scanned);
        Assert.Equal(1, result.Changed);
        Assert.Equal(wrong.Key, result.Changes.Single().Key);
        Assert.Equal(777m, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == completed.Key)).ShippedQuantity);
    }
}
