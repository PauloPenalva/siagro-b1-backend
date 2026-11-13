using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesCloseReopenServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<ShipmentRelease> SeedAsync(ReleaseStatus status)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private async Task<ShipmentRelease> ReloadAsync(Guid key) =>
        await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    [Theory]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    public async Task Close_ActivedOrPaused_BecomesCompleted(ReleaseStatus from)
    {
        var sr = await SeedAsync(from);

        await new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(sr.Key, "paulo.penalva");

        var reloaded = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Completed, reloaded.Status);
        Assert.Equal("paulo.penalva", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Close_PendingRelease_Throws()
    {
        var sr = await SeedAsync(ReleaseStatus.Pending);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(sr.Key, "tester"));
    }

    [Fact]
    public async Task Reopen_Completed_BecomesActived()
    {
        var sr = await SeedAsync(ReleaseStatus.Completed);

        await new ShipmentReleasesReopenService(_db.Context).ExecuteAsync(sr.Key, "tester");

        Assert.Equal(ReleaseStatus.Actived, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Reopen_NotCompleted_Throws()
    {
        var sr = await SeedAsync(ReleaseStatus.Actived);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ShipmentReleasesReopenService(_db.Context).ExecuteAsync(sr.Key, "tester"));
    }

    // ---------- GAC-1214: liberação de devolução e o saldo do contrato de compra ----------

    /// <summary>Contrato com compra de 100 alocada, devolução de 100 e liberação SalesReturn de 100.</summary>
    private async Task<(PurchaseContract Contract, StorageTransaction Generator, ShipmentRelease Release)> SeedReturnAsync(
        ReleaseStatus status = ReleaseStatus.Actived)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-RET", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            TotalVolume = 1_000m, Status = ContractStatus.Approved, AllocatedVolume = 100m,
        };
        StorageTransaction Tx(StorageTransactionType type, decimal weight, Guid? release = null) => new()
        {
            Key = Guid.NewGuid(), Code = Guid.NewGuid().ToString("N")[..8], CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "01", TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed, NetWeight = weight, GrossWeight = weight,
            ShipmentReleaseKey = release,
        };
        var purchase = Tx(StorageTransactionType.Purchase, 100m);
        var generator = Tx(StorageTransactionType.SalesShipmentReturn, 100m);
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = 100m, Status = status, Origin = ReleaseOrigin.SalesReturn,
            GeneratedByStorageTransactionKey = generator.Key,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.StorageTransactions.AddRange(purchase, generator);
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            PurchaseContractKey = contract.Key, StorageTransactionKey = purchase.Key, Volume = 100m,
            ApprovedAt = DateTime.Now, ApprovedBy = "tester",
        });
        await _db.Context.SaveChangesAsync();
        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, release.Key);
        return (contract, generator, release);
    }

    private Task<decimal> AllocatedAsync(Guid contractKey) =>
        _db.Context.PurchaseContracts.AsNoTracking().Where(c => c.Key == contractKey)
            .Select(c => c.AllocatedVolume).SingleAsync();

    [Fact]
    public async Task Close_SalesReturnReleaseWithOpenBalance_StopsGivingBalanceBack()
    {
        var (contract, _, release) = await SeedReturnAsync();
        Assert.Equal(0m, await AllocatedAsync(contract.Key));

        await new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(release.Key, "tester");

        Assert.Equal(100m, await AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Reopen_SalesReturnRelease_GivesItBackAgain()
    {
        var (contract, _, release) = await SeedReturnAsync();
        await new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(release.Key, "tester");
        Assert.Equal(100m, await AllocatedAsync(contract.Key));

        await new ShipmentReleasesReopenService(_db.Context).ExecuteAsync(release.Key, "tester");

        Assert.Equal(0m, await AllocatedAsync(contract.Key));
    }
}
