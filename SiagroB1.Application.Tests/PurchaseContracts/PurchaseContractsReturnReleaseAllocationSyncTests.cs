using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// GAC-1214: as alocações do contrato derivadas das liberações de devolução (SalesReturn).
/// </summary>
public class PurchaseContractsReturnReleaseAllocationSyncTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private sealed record Seed(PurchaseContract Contract, StorageTransaction Generator, ShipmentRelease Release);

    private async Task<PurchaseContract> AddContractAsync(string code, ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "01",
            Status = status,
            TotalVolume = 10_000m,
            AllocatedVolume = 0m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private async Task<StorageTransaction> AddTransactionAsync(
        StorageTransactionType type, decimal weight, Guid? releaseKey = null,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        var tx = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = Guid.NewGuid().ToString("N")[..8],
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = type,
            TransactionStatus = status,
            GrossWeight = weight,
            NetWeight = weight,
            ShipmentReleaseKey = releaseKey,
        };
        _db.Context.StorageTransactions.Add(tx);
        await _db.Context.SaveChangesAsync();
        return tx;
    }

    private async Task<ShipmentRelease> AddReleaseAsync(
        PurchaseContract contract, StorageTransaction generator, decimal quantity,
        ReleaseOrigin origin = ReleaseOrigin.SalesReturn, ReleaseStatus status = ReleaseStatus.Actived)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = quantity,
            Status = status,
            Origin = origin,
            GeneratedByStorageTransactionKey = generator.Key,
        };
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();
        return release;
    }

    /// <summary>Contrato de 10.000 com compra de 3.000 já alocada e devolução de 3.000.</summary>
    private async Task<Seed> SeedAsync(
        ReleaseOrigin origin = ReleaseOrigin.SalesReturn, ReleaseStatus status = ReleaseStatus.Actived,
        ContractStatus contractStatus = ContractStatus.Approved)
    {
        var contract = await AddContractAsync("PC-001", contractStatus);
        var purchase = await AddTransactionAsync(StorageTransactionType.Purchase, 3_000m);
        _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            PurchaseContractKey = contract.Key,
            StorageTransactionKey = purchase.Key,
            Volume = 3_000m,
            ApprovedAt = DateTime.Now,
            ApprovedBy = "tester",
        });
        contract.AllocatedVolume = 3_000m;
        await _db.Context.SaveChangesAsync();

        var generator = await AddTransactionAsync(StorageTransactionType.SalesShipmentReturn, 3_000m);
        var release = await AddReleaseAsync(contract, generator, 3_000m, origin, status);
        return new Seed(contract, generator, release);
    }

    private async Task<List<PurchaseContractAllocation>> AllocationsOfAsync(Guid contractKey, params Guid[] excludeTx) =>
        await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .Where(a => a.PurchaseContractKey == contractKey && !excludeTx.Contains(a.StorageTransactionKey))
            .ToListAsync();

    private async Task<PurchaseContract> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(c => c.Key == key);

    private async Task<decimal> VolumeOfAsync(Guid contractKey, Guid txKey) =>
        (await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(a => a.PurchaseContractKey == contractKey && a.StorageTransactionKey == txKey)).Volume;

    [Fact]
    public async Task Sync_LiveRelease_GivesTheBalanceBack()
    {
        var s = await SeedAsync();

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(-3_000m, await VolumeOfAsync(s.Contract.Key, s.Generator.Key));
        var contract = await ReloadAsync(s.Contract.Key);
        Assert.Equal(0m, contract.AllocatedVolume);
        Assert.Equal(10_000m, contract.AvaiableVolume);
    }

    [Fact]
    public async Task Sync_PartialShipment_ConsumesTheContractAgain()
    {
        var s = await SeedAsync();
        var shipment = await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(-3_000m, await VolumeOfAsync(s.Contract.Key, s.Generator.Key));
        Assert.Equal(1_000m, await VolumeOfAsync(s.Contract.Key, shipment.Key));
        Assert.Equal(1_000m, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Sync_WarehouseLoss_ConsumesLikeAShipment()
    {
        var s = await SeedAsync();
        var loss = await AddTransactionAsync(StorageTransactionType.WarehouseLoss, 200m, s.Release.Key);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(200m, await VolumeOfAsync(s.Contract.Key, loss.Key));
    }

    [Fact]
    public async Task Sync_ReturnAgainstTheRelease_GivesBack()
    {
        var s = await SeedAsync();
        await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);
        await AddTransactionAsync(StorageTransactionType.SalesShipmentReturn, 400m, s.Release.Key);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(600m, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Sync_CancelledShipment_RemovesItsAllocation()
    {
        var s = await SeedAsync();
        var shipment = await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);
        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        var tracked = await _db.Context.StorageTransactions.SingleAsync(t => t.Key == shipment.Key);
        tracked.TransactionStatus = StorageTransactionsStatus.Cancelled;
        await _db.Context.SaveChangesAsync();
        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.DoesNotContain(await AllocationsOfAsync(s.Contract.Key), x => x.StorageTransactionKey == shipment.Key);
        Assert.Equal(-3_000m, await VolumeOfAsync(s.Contract.Key, s.Generator.Key));
        Assert.Equal(0m, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Sync_CancelledGenerator_RemovesTheReturnAllocation()
    {
        var s = await SeedAsync();
        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        var tracked = await _db.Context.StorageTransactions.SingleAsync(t => t.Key == s.Generator.Key);
        tracked.TransactionStatus = StorageTransactionsStatus.Cancelled;
        await _db.Context.SaveChangesAsync();
        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(3_000m, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
        Assert.DoesNotContain(await AllocationsOfAsync(s.Contract.Key), a => a.StorageTransactionKey == s.Generator.Key);
    }

    [Theory]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Completed)]
    public async Task Sync_CancelledRelease_KeepsOnlyTheConsumedPart(ReleaseStatus status)
    {
        var s = await SeedAsync(status: status);
        await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(-1_000m, await VolumeOfAsync(s.Contract.Key, s.Generator.Key));
        Assert.Equal(3_000m, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Sync_CancelledReleaseWithoutShipments_LeavesNoGeneratorAllocation()
    {
        var s = await SeedAsync(status: ReleaseStatus.Cancelled);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.DoesNotContain(await AllocationsOfAsync(s.Contract.Key), a => a.StorageTransactionKey == s.Generator.Key);
        Assert.DoesNotContain(await _db.Context.PurchaseContractsAllocations.AsNoTracking().ToListAsync(), a => a.Volume == 0m);
    }

    [Fact]
    public async Task Sync_GeneratorSplitAcrossTwoContracts_EachGetsItsShare()
    {
        var contractA = await AddContractAsync("PC-A");
        var contractB = await AddContractAsync("PC-B");
        var generator = await AddTransactionAsync(StorageTransactionType.SalesShipmentReturn, 3_000m);
        var releaseA = await AddReleaseAsync(contractA, generator, 2_000m);
        var releaseB = await AddReleaseAsync(contractB, generator, 1_000m);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, releaseA.Key);
        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, releaseB.Key);

        Assert.Equal(-2_000m, await VolumeOfAsync(contractA.Key, generator.Key));
        Assert.Equal(-1_000m, await VolumeOfAsync(contractB.Key, generator.Key));
    }

    [Fact]
    public async Task Sync_Twice_IsIdempotent()
    {
        var s = await SeedAsync();
        await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);
        var count = await _db.Context.PurchaseContractsAllocations.CountAsync();
        var allocated = (await ReloadAsync(s.Contract.Key)).AllocatedVolume;

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(count, await _db.Context.PurchaseContractsAllocations.CountAsync());
        Assert.Equal(allocated, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
    }

    [Theory]
    [InlineData(ReleaseOrigin.Standard)]
    [InlineData(ReleaseOrigin.Transshipment)]
    [InlineData(ReleaseOrigin.WarehouseGain)]
    [InlineData(ReleaseOrigin.OwnershipTransfer)]
    public async Task Sync_NonSalesReturnOrigin_DoesNothing(ReleaseOrigin origin)
    {
        var s = await SeedAsync(origin);
        // Consumidor vivo (tipo 7) na liberação: sem a trava de origem, o sincronizador gravaria linhas.
        await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);
        var before = await _db.Context.PurchaseContractsAllocations.CountAsync();

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(before, await _db.Context.PurchaseContractsAllocations.CountAsync());
        Assert.Equal(3_000m, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Sync_FinishedContract_StillSyncs()
    {
        var s = await SeedAsync(contractStatus: ContractStatus.Finished);
        await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);

        await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, s.Release.Key);

        Assert.Equal(1_000m, (await ReloadAsync(s.Contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task SyncByGenerator_And_SyncContract_CoverAllReleases()
    {
        var s = await SeedAsync();
        await PurchaseContractsReturnReleaseAllocationSync.SyncByGeneratorAsync(_db.Context, s.Generator.Key);
        Assert.Equal(-3_000m, await VolumeOfAsync(s.Contract.Key, s.Generator.Key));

        var other = await AddContractAsync("PC-002");
        var generator2 = await AddTransactionAsync(StorageTransactionType.SalesShipmentReturn, 500m);
        await AddReleaseAsync(other, generator2, 500m);
        await PurchaseContractsReturnReleaseAllocationSync.SyncContractAsync(_db.Context, other.Key);

        Assert.Equal(-500m, await VolumeOfAsync(other.Key, generator2.Key));
        Assert.Equal(-500m, (await ReloadAsync(other.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task IsDerived_TrueForGeneratorAndConsumers_FalseForPurchase()
    {
        var s = await SeedAsync();
        var shipment = await AddTransactionAsync(StorageTransactionType.SalesShipment, 1_000m, s.Release.Key);
        var purchase = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .Select(a => a.StorageTransactionKey).FirstAsync();

        Assert.True(await PurchaseContractsReturnReleaseAllocationSync.IsDerivedAsync(_db.Context, s.Generator.Key));
        Assert.True(await PurchaseContractsReturnReleaseAllocationSync.IsDerivedAsync(_db.Context, shipment.Key));
        Assert.False(await PurchaseContractsReturnReleaseAllocationSync.IsDerivedAsync(_db.Context, purchase));
    }
}
