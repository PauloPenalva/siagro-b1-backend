using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

public class PurchaseContractsAllocationDischargeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsAllocationCreateService Service() => new(
        _db, new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance));

    /// <summary>
    /// AllocatedVolume também é seedado como escalar, mas <c>ApplyAllocationAsync</c> deriva o
    /// saldo do contrato somando as linhas PERSISTIDAS de <c>PurchaseContractsAllocations</c>
    /// (não o escalar) — ver "Allocation sums" em constraints.md e
    /// <c>PurchaseContractsAllocationCreateServiceTests.ExecuteReversalAsync_DecreasesContractAllocatedVolume</c>.
    /// Sem uma linha de alocação persistida por trás de <paramref name="allocated"/>, a próxima
    /// chamada recalcularia o saldo do zero e o escalar seedado seria descartado — por isso um
    /// romaneio de Compra (8) confirmado, já esgotado, sustenta o valor com uma alocação real.
    /// </summary>
    private async Task<PurchaseContract> SeedContractAsync(decimal total, decimal allocated,
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", Status = status,
            TotalVolume = total, AllocatedVolume = allocated, FreightTerms = FreightTerms.Cif,
            DeliveryLocationCode = "01",
        };
        _db.Context.PurchaseContracts.Add(contract);

        if (allocated != 0)
        {
            var existingPurchase = new StorageTransaction
            {
                Key = Guid.NewGuid(), Code = "P0", CardCode = "F0001", ItemCode = "SOJA",
                UnitOfMeasureCode = "KG", WarehouseCode = "01",
                TransactionType = StorageTransactionType.Purchase,
                TransactionStatus = StorageTransactionsStatus.Confirmed,
                NetWeight = allocated, AvaiableVolumeToAllocate = 0m,
            };
            _db.Context.StorageTransactions.Add(existingPurchase);
            _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
                StorageTransactionKey = existingPurchase.Key,
                Volume = allocated,
            });
        }

        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private static StorageTransaction NewDischarge(StorageTransactionType type, decimal qty) => new()
    {
        Key = Guid.NewGuid(), Code = "D1", CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
        WarehouseCode = "PORTO", TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
        GrossWeight = qty, NetWeight = qty, AvaiableVolumeToAllocate = qty,
    };

    private async Task<decimal> AllocatedAsync(Guid key) =>
        (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key)).AllocatedVolume;

    [Fact]
    public async Task Loss_is_allocated_negative_and_reopens_the_balance()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 1000m);
        var loss = NewDischarge(StorageTransactionType.PurchaseDischargeLoss, 30m);
        _db.Context.StorageTransactions.Add(loss);

        await Service().ExecuteDischargeAsync(contract.Key, loss, 30m, "tester");

        var alloc = await _db.Context.PurchaseContractsAllocations
            .SingleAsync(x => x.StorageTransactionKey == loss.Key);
        Assert.Equal(-30m, alloc.Volume);
        Assert.Equal(970m, await AllocatedAsync(contract.Key));
        Assert.Equal(0m, loss.AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task Gain_is_allocated_positive()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 900m);
        var gain = NewDischarge(StorageTransactionType.PurchaseDischargeGain, 20m);
        _db.Context.StorageTransactions.Add(gain);

        await Service().ExecuteDischargeAsync(contract.Key, gain, 20m, "tester");

        Assert.Equal(920m, await AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Gain_above_the_contract_balance_is_refused()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 990m);
        var gain = NewDischarge(StorageTransactionType.PurchaseDischargeGain, 20m);
        _db.Context.StorageTransactions.Add(gain);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteDischargeAsync(contract.Key, gain, 20m, "tester"));
    }

    [Fact]
    public async Task Loss_has_no_contract_ceiling()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 1000m);
        var loss = NewDischarge(StorageTransactionType.PurchaseDischargeLoss, 50m);
        _db.Context.StorageTransactions.Add(loss);

        await Service().ExecuteDischargeAsync(contract.Key, loss, 50m, "tester");

        Assert.Equal(950m, await AllocatedAsync(contract.Key));
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain)]
    public async Task Finished_contract_is_refused(StorageTransactionType type)
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 500m, status: ContractStatus.Finished);
        var tx = NewDischarge(type, 10m);
        _db.Context.StorageTransactions.Add(tx);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteDischargeAsync(contract.Key, tx, 10m, "tester"));
        Assert.Contains("encerrado", ex.Message);
    }

    [Fact]
    public async Task Other_types_are_refused()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 0m);
        var tx = NewDischarge(StorageTransactionType.Purchase, 10m);
        _db.Context.StorageTransactions.Add(tx);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteDischargeAsync(contract.Key, tx, 10m, "tester"));
    }
}
