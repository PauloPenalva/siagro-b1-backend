using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsRecalculateBalanceServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsRecalculateBalanceService Service() => new(_db.Context);

    internal static SalesContract NewContract(
        decimal totalVolume, decimal allocatedVolume,
        ContractStatus status = ContractStatus.Approved, decimal price = 100m) => new()
    {
        Key = Guid.NewGuid(),
        Code = Guid.NewGuid().ToString("N")[..8],
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = totalVolume,
        AllocatedVolume = allocatedVolume,
        Price = price,
        Status = status,
    };

    private SalesInvoiceItem NewItem(
        decimal quantity,
        SalesInvoiceDeliveryStatus deliveryStatus = SalesInvoiceDeliveryStatus.Open,
        decimal delivered = 0m, decimal loss = 0m) => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        Quantity = quantity,
        DeliveredQuantity = delivered,
        QuantityLoss = loss,
        DeliveryStatus = deliveryStatus,
    };

    private SalesContractAllocation NewAllocation(
        Guid contractKey, Guid itemKey, decimal volume, bool owner = false) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contractKey,
        SalesInvoiceItemKey = itemKey,
        Volume = volume,
        Origin = SalesContractAllocationOrigin.Billing,
        OwnsDeliveryDifference = owner,
    };

    private async Task<decimal> AllocatedAsync(Guid key) =>
        (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == key)).AllocatedVolume;

    [Fact]
    public async Task ExecuteAsync_CorrectsDivergentAllocatedVolume_AndReportsBeforeAfter()
    {
        var sc = NewContract(totalVolume: 5000m, allocatedVolume: 999m); // valor errado
        var i1 = NewItem(300m);
        var i2 = NewItem(200m);
        _db.Context.SalesContracts.Add(sc);
        _db.Context.SalesInvoicesItems.AddRange(i1, i2);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(sc.Key, i1.Key!.Value, 300m),
            NewAllocation(sc.Key, i2.Key!.Value, 200m));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(sc.Key);

        Assert.True(result.Changed);
        Assert.Equal(999m, result.PreviousAllocatedVolume);
        Assert.Equal(500m, result.NewAllocatedVolume);
        Assert.Equal(4500m, result.NewAvaiableVolume); // 5000 − 500
        Assert.Equal(500m, await AllocatedAsync(sc.Key));
    }

    [Fact]
    public async Task ExecuteAsync_ClosedItemWithLoss_SubtractsShortageFromOwnerLine()
    {
        // Item fechado com quebra: líquido 90 de 100 faturados (entregue 95, perda 5). A
        // quebra de 10 sai da linha dona, que aqui é a única do item.
        var sc = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var item = NewItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 95m, loss: 5m);
        _db.Context.SalesContracts.Add(sc);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.Add(
            NewAllocation(sc.Key, item.Key!.Value, 100m, owner: true));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(sc.Key);

        Assert.Equal(90m, result.NewAllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_ReallocatedItemWithLoss_ConcentratesShortageOnOwnerContract()
    {
        // Item de 100 dividido 60/40 entre dois contratos, com quebra de 10 no fechamento.
        // A quebra NÃO é rateada: sai inteira do contrato da linha dona (A → 50), e o outro
        // consome o nominal (B → 40).
        var a = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var b = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var item = NewItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 90m, loss: 0m);
        _db.Context.SalesContracts.AddRange(a, b);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(a.Key, item.Key!.Value, 60m, owner: true),
            NewAllocation(b.Key, item.Key!.Value, 40m));
        await _db.Context.SaveChangesAsync();

        Assert.Equal(50m, (await Service().ExecuteAsync(a.Key)).NewAllocatedVolume);
        Assert.Equal(40m, (await Service().ExecuteAsync(b.Key)).NewAllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_ReallocatedItemWithLoss_PreservesTotalConsumption()
    {
        // Rede de segurança da feature: concentrar só muda a REPARTIÇÃO. O total consumido
        // pelos contratos que dividem o item continua sendo o líquido entregue.
        var a = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var b = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var item = NewItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 92m, loss: 2m);
        _db.Context.SalesContracts.AddRange(a, b);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(a.Key, item.Key!.Value, 60m, owner: true),
            NewAllocation(b.Key, item.Key!.Value, 40m));
        await _db.Context.SaveChangesAsync();

        var totalA = (await Service().ExecuteAsync(a.Key)).NewAllocatedVolume;
        var totalB = (await Service().ExecuteAsync(b.Key)).NewAllocatedVolume;

        Assert.Equal(item.NetQuantity, totalA + totalB); // 90 = 100 − (100 − 90)
    }

    [Fact]
    public async Task ExecuteAsync_OpenItem_ConsumesNominal_EvenOnOwnerLine()
    {
        // Sem conferência não há quebra apurada: a linha dona consome o nominal.
        var sc = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var item = NewItem(100m, SalesInvoiceDeliveryStatus.Open, delivered: 90m, loss: 5m);
        _db.Context.SalesContracts.Add(sc);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.Add(
            NewAllocation(sc.Key, item.Key!.Value, 100m, owner: true));
        await _db.Context.SaveChangesAsync();

        Assert.Equal(100m, (await Service().ExecuteAsync(sc.Key)).NewAllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_NonOwnerLineOfClosedItem_NeverSubtractsShortage()
    {
        // Contrato que só tem a linha NÃO dona do item consome o nominal puro, mesmo com o
        // item fechado com quebra.
        var owner = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var other = NewContract(totalVolume: 1000m, allocatedVolume: 0m);
        var item = NewItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 70m, loss: 0m);
        _db.Context.SalesContracts.AddRange(owner, other);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(owner.Key, item.Key!.Value, 50m, owner: true),
            NewAllocation(other.Key, item.Key!.Value, 50m));
        await _db.Context.SaveChangesAsync();

        Assert.Equal(50m, (await Service().ExecuteAsync(other.Key)).NewAllocatedVolume);
        Assert.Equal(20m, (await Service().ExecuteAsync(owner.Key)).NewAllocatedVolume); // 50 − 30
    }

    [Fact]
    public async Task ExecuteAsync_FinishedContract_Throws()
    {
        var sc = NewContract(totalVolume: 5000m, allocatedVolume: 500m, status: ContractStatus.Finished);
        _db.Context.SalesContracts.Add(sc);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(sc.Key));
    }

    [Fact]
    public async Task ExecuteAsync_NotFound_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service().ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExecuteAllAsync_RecalculatesNonFinished_ExcludesFinished_ListsChanged()
    {
        var okItem = NewItem(100m);
        var ok = NewContract(totalVolume: 1000m, allocatedVolume: 100m); // correto
        _db.Context.SalesContracts.Add(ok);
        _db.Context.SalesInvoicesItems.Add(okItem);
        _db.Context.SalesContractsAllocations.Add(NewAllocation(ok.Key, okItem.Key!.Value, 100m));

        var wrongItem = NewItem(250m);
        var wrong = NewContract(totalVolume: 1000m, allocatedVolume: 0m); // divergente
        _db.Context.SalesContracts.Add(wrong);
        _db.Context.SalesInvoicesItems.Add(wrongItem);
        _db.Context.SalesContractsAllocations.Add(NewAllocation(wrong.Key, wrongItem.Key!.Value, 250m));

        var finished = NewContract(totalVolume: 1000m, allocatedVolume: 777m, status: ContractStatus.Finished);
        _db.Context.SalesContracts.Add(finished);

        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAllAsync();

        Assert.Equal(2, result.Scanned);
        Assert.Equal(1, result.Changed);
        Assert.Single(result.Changes);
        Assert.Equal(wrong.Key, result.Changes.First().Key);
        Assert.Equal(250m, result.Changes.First().NewAllocatedVolume);
        Assert.Equal(777m, await AllocatedAsync(finished.Key)); // finished intocado
    }
}
