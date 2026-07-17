using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesCancelationServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesCancelationService Service() =>
        new(_db.Context, NullLogger<ShipmentReleasesCancelationService>.Instance);

    private async Task<ShipmentRelease> SeedReleaseAsync()
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01", ReleasedQuantity = 100m, Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    [Fact]
    public async Task Cancel_WithLiveTransaction_ThrowsListingCodes()
    {
        var sr = await SeedReleaseAsync();
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "ST-777", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "01",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentReleaseKey = sr.Key,
        });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(sr.Key));
        Assert.Contains("ST-777", ex.Message);
    }

    [Fact]
    public async Task Cancel_NoTransactions_Cancels()
    {
        var sr = await SeedReleaseAsync();

        await Service().ExecuteAsync(sr.Key);

        Assert.Equal(ReleaseStatus.Cancelled, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == sr.Key)).Status);
    }
}
