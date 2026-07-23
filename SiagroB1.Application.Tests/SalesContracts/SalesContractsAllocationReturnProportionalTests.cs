using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsAllocationReturnProportionalTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsAllocationCreateForReturnService Service() =>
        new(_db, new SalesContractsFixedVolumeService(_db.Context));

    private SalesContract _a = null!;
    private SalesContract _b = null!;
    private SalesShipmentRelease _releaseA = null!;
    private SalesShipmentRelease _releaseB = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;

    /// <summary>
    /// Item de 100 faturado no contrato A e parcialmente realocado para B (60/40).
    /// </summary>
    private async Task SeedReallocatedItemAsync()
    {
        _a = NewContract(totalVolume: 1000m, price: 100m);
        _b = NewContract(totalVolume: 1000m, price: 120m);
        _releaseA = NewRelease(_a.Key, released: 500m, shipped: 60m);
        _releaseB = NewRelease(_b.Key, released: 500m, shipped: 40m);
        _invoice = NewInvoice();
        _item = NewItem(_invoice, _a.Key, _releaseA.Key, quantity: 100m, unitPrice: 90m);
        _a.AllocatedVolume = 60m;
        _b.AllocatedVolume = 40m;

        var group = Guid.NewGuid();
        _db.Context.AddRange(_a, _b, _releaseA, _releaseB, _invoice);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(_a.Key, _item.Key!.Value, 100m, _releaseA.Key),
            NewAllocation(_a.Key, _item.Key!.Value, -40m, _releaseA.Key,
                origin: SalesContractAllocationOrigin.Reallocation, groupKey: group),
            NewAllocation(_b.Key, _item.Key!.Value, 40m, _releaseB.Key,
                origin: SalesContractAllocationOrigin.Reallocation, contractPrice: 120m, groupKey: group));
        await _db.Context.SaveChangesAsync();
    }

    private (SalesInvoice ReturnInvoice, SalesInvoiceItem ReturnItem) NewReturn(decimal quantity)
    {
        var returnInvoice = NewInvoice(type: SalesInvoiceType.Return, originKey: _invoice.Key);
        var returnItem = NewItem(returnInvoice, _a.Key, null, quantity, unitPrice: 90m,
            originItemKey: _item.Key);
        _db.Context.Add(returnInvoice);
        return (returnInvoice, returnItem);
    }

    [Fact]
    public async Task Execute_FullReturn_MirrorsCurrentDistribution()
    {
        await SeedReallocatedItemAsync();
        var (returnInvoice, returnItem) = NewReturn(100m);
        await _db.Context.SaveChangesAsync();

        var affected = await Service().ExecuteAsync(returnInvoice, "tester");

        var rows = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(x => x.SalesInvoiceItemKey == returnItem.Key)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(-60m, rows.Single(r => r.SalesContractKey == _a.Key).Volume);
        Assert.Equal(-40m, rows.Single(r => r.SalesContractKey == _b.Key).Volume);
        Assert.Equal(_releaseA.Key, rows.Single(r => r.SalesContractKey == _a.Key).SalesShipmentReleaseKey);
        Assert.Equal(_releaseB.Key, rows.Single(r => r.SalesContractKey == _b.Key).SalesShipmentReleaseKey);
        Assert.Equal(new HashSet<Guid> { _releaseA.Key, _releaseB.Key }, affected);

        // Devolução total zera o consumo dos dois contratos.
        Assert.Equal(0m, (await ContractAsync(_db, _a.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractAsync(_db, _b.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Execute_FullReturn_NetsPriceDifferenceToZero()
    {
        await SeedReallocatedItemAsync();
        var (returnInvoice, _) = NewReturn(100m);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(returnInvoice, "tester");

        var netA = await _db.Context.SalesContractsAllocations
            .Where(x => x.SalesContractKey == _a.Key).SumAsync(x => x.PriceDifference);
        var netB = await _db.Context.SalesContractsAllocations
            .Where(x => x.SalesContractKey == _b.Key).SumAsync(x => x.PriceDifference);
        Assert.Equal(0m, netA);
        Assert.Equal(0m, netB);
    }

    [Fact]
    public async Task Execute_PartialReturn_DistributesProportionally_WithRoundingResidue()
    {
        await SeedReallocatedItemAsync();
        var (returnInvoice, returnItem) = NewReturn(10m);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(returnInvoice, "tester");

        var rows = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(x => x.SalesInvoiceItemKey == returnItem.Key)
            .ToListAsync();
        // 10 × 60/100 = 6 e 10 × 40/100 = 4; Σ exata = 10.
        Assert.Equal(-6m, rows.Single(r => r.SalesContractKey == _a.Key).Volume);
        Assert.Equal(-4m, rows.Single(r => r.SalesContractKey == _b.Key).Volume);
        Assert.Equal(-10m, rows.Sum(r => r.Volume));
    }

    [Fact]
    public async Task Execute_ThirdsRounding_ResidueGoesToLargestShare_SumStaysExact()
    {
        // Distribuição 2/3 vs 1/3 com devolução de 10 → parcelas 6,667/3,333.
        _a = NewContract(totalVolume: 1000m);
        _b = NewContract(totalVolume: 1000m);
        _invoice = NewInvoice();
        _item = NewItem(_invoice, _a.Key, null, quantity: 90m, unitPrice: 90m);
        var group = Guid.NewGuid();
        _db.Context.AddRange(_a, _b, _invoice);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(_a.Key, _item.Key!.Value, 90m),
            NewAllocation(_a.Key, _item.Key!.Value, -30m,
                origin: SalesContractAllocationOrigin.Reallocation, groupKey: group),
            NewAllocation(_b.Key, _item.Key!.Value, 30m,
                origin: SalesContractAllocationOrigin.Reallocation, groupKey: group));
        var (returnInvoice, returnItem) = NewReturn(10m);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(returnInvoice, "tester");

        var rows = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(x => x.SalesInvoiceItemKey == returnItem.Key)
            .ToListAsync();
        Assert.Equal(-10m, rows.Sum(r => r.Volume)); // Σ exata apesar do arredondamento
        Assert.Equal(-6.667m, rows.Single(r => r.SalesContractKey == _a.Key).Volume);
        Assert.Equal(-3.333m, rows.Single(r => r.SalesContractKey == _b.Key).Volume);
    }

    [Fact]
    public async Task Execute_IsIdempotentPerReturnItem()
    {
        await SeedReallocatedItemAsync();
        var (returnInvoice, returnItem) = NewReturn(100m);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(returnInvoice, "tester");
        await Service().ExecuteAsync(returnInvoice, "tester");

        Assert.Equal(2, await _db.Context.SalesContractsAllocations
            .CountAsync(x => x.SalesInvoiceItemKey == returnItem.Key));
    }

    [Fact]
    public async Task Execute_SecondPartialReturn_UsesRemainingNetBasis()
    {
        await SeedReallocatedItemAsync();
        var (firstReturn, _) = NewReturn(50m);
        await _db.Context.SaveChangesAsync();
        await Service().ExecuteAsync(firstReturn, "tester");

        // Segunda devolução dos 50 restantes: base líquida 30(A)/20(B) → −30/−20.
        var (secondReturn, secondItem) = NewReturn(50m);
        await _db.Context.SaveChangesAsync();
        await Service().ExecuteAsync(secondReturn, "tester");

        var rows = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(x => x.SalesInvoiceItemKey == secondItem.Key)
            .ToListAsync();
        Assert.Equal(-30m, rows.Single(r => r.SalesContractKey == _a.Key).Volume);
        Assert.Equal(-20m, rows.Single(r => r.SalesContractKey == _b.Key).Volume);

        Assert.Equal(0m, (await ContractAsync(_db, _a.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractAsync(_db, _b.Key)).AllocatedVolume);
    }
}
