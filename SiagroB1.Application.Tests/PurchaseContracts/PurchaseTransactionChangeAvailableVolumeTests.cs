using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseTransactionChangeAvailableVolumeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static StorageTransaction NewStorageTransaction(
        StorageTransactionType type, decimal netWeight, Guid? changeKey = null) => new()
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
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        NetWeight = netWeight,
        PurchaseTransactionChangeKey = changeKey,
    };

    [Fact]
    public void Recalculate_SubtractsChangedQuantity()
    {
        var st = NewStorageTransaction(StorageTransactionType.Purchase, 30000m);

        st.RecalculateAvailableVolume(10000m, 20000m);
        Assert.Equal(0m, st.AvaiableVolumeToAllocate);

        st.RecalculateAvailableVolume(5000m, 20000m);
        Assert.Equal(5000m, st.AvaiableVolumeToAllocate);
    }

    [Fact]
    public void Recalculate_ChangeReturnIsNeverAllocatable()
    {
        var marked = NewStorageTransaction(StorageTransactionType.PurchaseReturn, 20000m, Guid.NewGuid());
        marked.RecalculateAvailableVolume(0m);
        Assert.Equal(0m, marked.AvaiableVolumeToAllocate);

        var plain = NewStorageTransaction(StorageTransactionType.PurchaseReturn, 20000m);
        plain.RecalculateAvailableVolume(5000m);
        Assert.Equal(15000m, plain.AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task DeleteAllocationAfterChange_FreesOnlyAllocatedVolume()
    {
        var original = NewStorageTransaction(StorageTransactionType.Purchase, 30000m);
        var ret = NewStorageTransaction(StorageTransactionType.PurchaseReturn, 20000m);
        var neu = NewStorageTransaction(StorageTransactionType.Purchase, 20000m);
        var contractA = Guid.NewGuid();
        var alloc = new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contractA,
            StorageTransactionKey = original.Key,
            Volume = 10000m,
        };
        var change = new PurchaseTransactionChange
        {
            Key = Guid.NewGuid(),
            OriginalStorageTransactionKey = original.Key,
            ReturnStorageTransactionKey = ret.Key,
            NewStorageTransactionKey = neu.Key,
            PurchaseContractKey = contractA,
            OriginalCardCode = "F0001",
            OriginalWarehouseCode = "01",
            NewCardCode = "F0002",
            NewWarehouseCode = "02",
            Quantity = 20000m,
            AllocatedVolume = 20000m,
            Reason = "Fornecedor trocado",
        };
        _db.Context.StorageTransactions.AddRange(original, ret, neu);
        _db.Context.PurchaseContractsAllocations.Add(alloc);
        _db.Context.PurchaseTransactionChanges.Add(change);
        await _db.Context.SaveChangesAsync();

        var service = new PurchaseContractsAllocationDeleteService(_db, new TestLogger<PurchaseContractsAllocationDeleteService>());
        await service.ExecuteAsync(alloc.Key, "tester");

        var available = (await _db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == original.Key)).AvaiableVolumeToAllocate;
        Assert.Equal(10000m, available);
    }
}
