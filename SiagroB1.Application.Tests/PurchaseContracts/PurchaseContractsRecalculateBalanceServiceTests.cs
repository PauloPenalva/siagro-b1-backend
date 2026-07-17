using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsRecalculateBalanceServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsRecalculateBalanceService Service() => new(_db.Context);

    private static PurchaseContract NewContract(
        decimal totalVolume, decimal allocatedVolume,
        ContractStatus status = ContractStatus.Approved) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
        AllocatedVolume = allocatedVolume,
        Status = status,
    };

    private PurchaseContractAllocation NewAllocation(Guid contractKey, decimal volume) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contractKey,
        StorageTransactionKey = Guid.NewGuid(),
        Volume = volume,
    };

    [Fact]
    public async Task ExecuteAsync_CorrectsDivergentAllocatedVolume_AndReportsBeforeAfter()
    {
        var pc = NewContract(totalVolume: 5000m, allocatedVolume: 999m); // valor errado
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractsAllocations.AddRange(
            NewAllocation(pc.Key, 300m), NewAllocation(pc.Key, 200m));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(pc.Key);

        Assert.True(result.Changed);
        Assert.Equal(999m, result.PreviousAllocatedVolume);
        Assert.Equal(500m, result.NewAllocatedVolume);
        Assert.Equal(4500m, result.NewAvaiableVolume); // 5000 − 500
        Assert.Equal(500m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == pc.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCorrect_ReturnsChangedFalse()
    {
        var pc = NewContract(totalVolume: 5000m, allocatedVolume: 500m);
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractsAllocations.AddRange(
            NewAllocation(pc.Key, 300m), NewAllocation(pc.Key, 200m));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(pc.Key);

        Assert.False(result.Changed);
        Assert.Equal(500m, result.NewAllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_FinishedContract_Throws()
    {
        var pc = NewContract(totalVolume: 5000m, allocatedVolume: 500m, status: ContractStatus.Finished);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(pc.Key));
    }

    [Fact]
    public async Task ExecuteAsync_NotFound_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service().ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExecuteAllAsync_RecalculatesNonFinished_ExcludesFinished_ListsChanged()
    {
        var ok = NewContract(totalVolume: 1000m, allocatedVolume: 100m); // correto
        _db.Context.PurchaseContracts.Add(ok);
        _db.Context.PurchaseContractsAllocations.Add(NewAllocation(ok.Key, 100m));

        var wrong = NewContract(totalVolume: 1000m, allocatedVolume: 0m);  // divergente
        _db.Context.PurchaseContracts.Add(wrong);
        _db.Context.PurchaseContractsAllocations.Add(NewAllocation(wrong.Key, 250m));

        var finished = NewContract(totalVolume: 1000m, allocatedVolume: 777m, status: ContractStatus.Finished);
        _db.Context.PurchaseContracts.Add(finished);
        _db.Context.PurchaseContractsAllocations.Add(NewAllocation(finished.Key, 100m));

        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAllAsync();

        Assert.Equal(2, result.Scanned);                 // ok + wrong (finished excluído)
        Assert.Equal(1, result.Changed);                 // só o wrong divergia
        Assert.Single(result.Changes);
        Assert.Equal(wrong.Key, result.Changes.First().Key);
        Assert.Equal(250m, result.Changes.First().NewAllocatedVolume);
        // finished intocado
        Assert.Equal(777m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == finished.Key)).AllocatedVolume);
    }
}
