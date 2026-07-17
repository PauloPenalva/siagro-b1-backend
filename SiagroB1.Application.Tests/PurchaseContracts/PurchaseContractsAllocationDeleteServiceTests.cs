using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsAllocationDeleteServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly TestLogger<PurchaseContractsAllocationDeleteService> _logger = new();

    private PurchaseContractsAllocationDeleteService CreateService() => new(_db, _logger);

    private static StorageTransaction NewStorageTransaction(
        decimal netWeight = 0,
        decimal availableVolume = 0,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        StorageTransactionType type = StorageTransactionType.Purchase) => new()
    {
        Key = Guid.NewGuid(),
        Code = "ST-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TruckCode = "TRK01",
        ProcessingCostCode = "PC01",
        TransactionType = type,
        TransactionStatus = status,
        NetWeight = netWeight,
        AvaiableVolumeToAllocate = availableVolume,
    };

    private PurchaseContractAllocation NewAllocation(StorageTransaction st, decimal volume) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = Guid.NewGuid(),
        StorageTransactionKey = st.Key,
        Volume = volume,
    };

    private async Task SeedAsync(StorageTransaction st, params PurchaseContractAllocation[] allocs)
    {
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContractsAllocations.AddRange(allocs);
        await _db.Context.SaveChangesAsync();
    }

    private async Task<decimal> AvailableAsync() =>
        (await _db.Context.StorageTransactions.AsNoTracking().SingleAsync()).AvaiableVolumeToAllocate;

    private async Task<PurchaseContract> SeedContractAsync(decimal totalVolume)
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-DEL",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = totalVolume,
            AllocatedVolume = totalVolume, // será recalculado no delete
        };
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    [Fact]
    public async Task ExecuteAsync_AllocationNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().ExecuteAsync(Guid.NewGuid(), "tester"));
    }

    [Fact]
    public async Task ExecuteAsync_StorageTransactionNotFound_ThrowsNotFoundException()
    {
        var alloc = new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            StorageTransactionKey = Guid.NewGuid(), // não existe
            Volume = 100m,
        };
        _db.Context.PurchaseContractsAllocations.Add(alloc);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().ExecuteAsync(alloc.Key, "tester"));
    }

    [Fact]
    public async Task ExecuteAsync_InvoicedStorageTransaction_ThrowsAndKeepsAllocation()
    {
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 900m,
            status: StorageTransactionsStatus.Invoiced);
        var alloc = NewAllocation(st, volume: 100m);
        await SeedAsync(st, alloc);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CreateService().ExecuteAsync(alloc.Key, "tester"));

        Assert.Equal(1, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());
        Assert.Equal(900m, await AvailableAsync());
    }

    [Fact]
    public async Task ExecuteAsync_DeletesAllocation_AndDerivesAvailableFromRemainingAllocations()
    {
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 900m);
        var alloc = NewAllocation(st, volume: 100m);
        await SeedAsync(st, alloc);

        await CreateService().ExecuteAsync(alloc.Key, "tester");

        Assert.Equal(0, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());
        // NetWeight 1000 − Σ(0 restante) = 1000
        Assert.Equal(1000m, await AvailableAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WithOtherAllocationsRemaining_DerivesFromTheirSum()
    {
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 650m);
        var toDelete = NewAllocation(st, volume: 100m);
        var keep = NewAllocation(st, volume: 250m);
        await SeedAsync(st, toDelete, keep);

        await CreateService().ExecuteAsync(toDelete.Key, "tester");

        // NetWeight 1000 − Σ(|250|) = 750
        Assert.Equal(750m, await AvailableAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SelfHealsCorruptedAvailableVolume()
    {
        // Saldo persistido deliberadamente errado — a semântica derivada corrige
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 555m);
        var alloc = NewAllocation(st, volume: 100m);
        await SeedAsync(st, alloc);

        await CreateService().ExecuteAsync(alloc.Key, "tester");

        Assert.Equal(1000m, await AvailableAsync());
    }

    [Fact]
    public async Task ExecuteAsync_NegativeVolumeAllocation_UsesAbsoluteInSum()
    {
        // Devolução (PurchaseReturn) é gravada com volume negativo
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 960m);
        var ret = NewAllocation(st, volume: -40m);
        var keep = NewAllocation(st, volume: 100m);
        await SeedAsync(st, ret, keep);

        await CreateService().ExecuteAsync(ret.Key, "tester");

        // NetWeight 1000 − Σ(|100|) = 900
        Assert.Equal(900m, await AvailableAsync());
    }

    [Fact]
    public async Task ExecuteAsync_DeferredCommit_DoesNotPersistUntilCallerSaves()
    {
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 900m);
        var alloc = NewAllocation(st, volume: 100m);
        await SeedAsync(st, alloc);

        await CreateService().ExecuteAsync(alloc.Key, "tester", CommitMode.Deferred);

        Assert.Equal(1, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());

        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());
        Assert.Equal(1000m, await AvailableAsync());
    }

    [Fact]
    public async Task ExecuteWithTransactionAsync_DeletesAllocation()
    {
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 900m);
        var alloc = NewAllocation(st, volume: 100m);
        await SeedAsync(st, alloc);

        await CreateService().ExecuteWithTransactionAsync(alloc.Key, "tester");

        Assert.Equal(0, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());
        Assert.Equal(1000m, await AvailableAsync());
    }

    [Fact]
    public async Task ExecuteWithTransactionAsync_PropagatesOriginalExceptionType()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().ExecuteWithTransactionAsync(Guid.NewGuid(), "tester"));
    }

    [Fact]
    public async Task ExecuteAsync_LogsWhoDeleted()
    {
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 990m);
        var alloc = NewAllocation(st, volume: 10m);
        await SeedAsync(st, alloc);

        await CreateService().ExecuteAsync(alloc.Key, "paulo.penalva");

        Assert.Contains(_logger.Entries, e => e.Message.Contains("paulo.penalva")
                                              && e.Message.Contains(alloc.Key.ToString()));
    }

    [Fact]
    public async Task ExecuteAsync_RecalculatesContractAllocatedVolume_FromRemainingSigned()
    {
        var pc = await SeedContractAsync(totalVolume: 1000m);
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 650m);

        var toDelete = NewAllocation(st, volume: 100m);
        toDelete.PurchaseContractKey = pc.Key;
        var keep = NewAllocation(st, volume: 250m);
        keep.PurchaseContractKey = pc.Key;
        await SeedAsync(st, toDelete, keep);

        await CreateService().ExecuteAsync(toDelete.Key, "tester");

        var contract = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == pc.Key);
        Assert.Equal(250m, contract.AllocatedVolume); // Σ(250) restante, com sinal
        Assert.Equal(750m, contract.AvaiableVolume);  // 1000 − 250
    }
}
