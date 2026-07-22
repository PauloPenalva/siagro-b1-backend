using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsReallocationDeleteServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsReallocationDeleteService Service() =>
        new(_db, new TestLogger<SalesContractsReallocationDeleteService>());

    private SalesContract _a = null!;
    private SalesContract _b = null!;
    private SalesShipmentRelease _releaseA = null!;
    private SalesShipmentRelease _releaseB = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;
    private Guid _groupKey;
    private SalesContractAllocation _positiveRow = null!;

    /// <summary>
    /// Item de 200 no contrato A, com realocação de 80 para B já efetivada.
    /// </summary>
    private async Task SeedAsync()
    {
        _a = NewContract(totalVolume: 1000m, price: 100m);
        _b = NewContract(totalVolume: 1000m, price: 120m);
        _releaseA = NewRelease(_a.Key, released: 500m, shipped: 120m);
        _releaseB = NewRelease(_b.Key, released: 500m, shipped: 80m);
        _invoice = NewInvoice();
        _item = NewItem(_invoice, _a.Key, _releaseA.Key, quantity: 200m, unitPrice: 90m);
        _a.AllocatedVolume = 120m;
        _b.AllocatedVolume = 80m;

        _groupKey = Guid.NewGuid();
        var negativeRow = NewAllocation(_a.Key, _item.Key!.Value, -80m, _releaseA.Key,
            origin: SalesContractAllocationOrigin.Reallocation, groupKey: _groupKey);
        _positiveRow = NewAllocation(_b.Key, _item.Key!.Value, 80m, _releaseB.Key,
            origin: SalesContractAllocationOrigin.Reallocation, contractPrice: 120m, groupKey: _groupKey);

        _db.Context.AddRange(_a, _b, _releaseA, _releaseB, _invoice);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(_a.Key, _item.Key!.Value, 200m, _releaseA.Key),
            negativeRow, _positiveRow);
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Execute_RemovesWholeGroup_AndRestoresBalances()
    {
        await SeedAsync();

        await Service().ExecuteWithTransactionAsync(_positiveRow.Key, "tester");

        Assert.Equal(0, await _db.Context.SalesContractsAllocations
            .CountAsync(a => a.ReallocationGroupKey == _groupKey));
        Assert.Equal(200m, (await ContractAsync(_db, _a.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractAsync(_db, _b.Key)).AllocatedVolume);
        Assert.Equal(200m, (await ReleaseAsync(_db, _releaseA.Key)).ShippedQuantity);
        Assert.Equal(0m, (await ReleaseAsync(_db, _releaseB.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Execute_NonReallocationRow_Throws()
    {
        await SeedAsync();
        var billingRow = await _db.Context.SalesContractsAllocations
            .SingleAsync(a => a.Origin == SalesContractAllocationOrigin.Billing);

        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteWithTransactionAsync(billingRow.Key, "tester"));
    }

    [Fact]
    public async Task Execute_NotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service().ExecuteWithTransactionAsync(Guid.NewGuid(), "tester"));
    }

    [Fact]
    public async Task Execute_ChainedReallocationDependsOnVolume_Throws()
    {
        await SeedAsync();

        // Realocação encadeada: B → C consumiu os 80 que chegaram em B.
        var c = NewContract(totalVolume: 1000m, price: 110m);
        var releaseC = NewRelease(c.Key, released: 500m, shipped: 80m);
        var chainedGroup = Guid.NewGuid();
        _db.Context.AddRange(c, releaseC);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(_b.Key, _item.Key!.Value, -80m, _releaseB.Key,
                origin: SalesContractAllocationOrigin.Reallocation, groupKey: chainedGroup),
            NewAllocation(c.Key, _item.Key!.Value, 80m, releaseC.Key,
                origin: SalesContractAllocationOrigin.Reallocation, contractPrice: 110m, groupKey: chainedGroup));
        await _db.Context.SaveChangesAsync();

        // Estornar a PRIMEIRA realocação deixaria (B, releaseB) negativo.
        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteWithTransactionAsync(_positiveRow.Key, "tester"));
        Assert.Contains("realocações posteriores", ex.Message);
    }

    [Fact]
    public async Task Execute_CancelledInvoice_Throws()
    {
        await SeedAsync();
        var invoice = await _db.Context.SalesInvoices.SingleAsync(i => i.Key == _invoice.Key);
        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteWithTransactionAsync(_positiveRow.Key, "tester"));
    }

    [Fact]
    public async Task Execute_FinishedContract_Throws()
    {
        await SeedAsync();
        var contract = await _db.Context.SalesContracts.SingleAsync(x => x.Key == _b.Key);
        contract.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteWithTransactionAsync(_positiveRow.Key, "tester"));
        Assert.Contains("encerrado", ex.Message);
    }

    [Theory]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Completed)]
    public async Task Execute_OriginReleaseNotReabsorbable_Throws(ReleaseStatus status)
    {
        await SeedAsync();
        var release = await _db.Context.SalesShipmentReleases.SingleAsync(r => r.Key == _releaseA.Key);
        release.Status = status;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteWithTransactionAsync(_positiveRow.Key, "tester"));
    }

    [Fact]
    public async Task Execute_OriginReleaseWithoutBalanceToReabsorb_Throws()
    {
        await SeedAsync();
        var release = await _db.Context.SalesShipmentReleases.SingleAsync(r => r.Key == _releaseA.Key);
        release.ReleasedQuantity = 150m; // saldo 30 < 80 a reabsorver
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteWithTransactionAsync(_positiveRow.Key, "tester"));
        Assert.Contains("reabsorver", ex.Message);
    }
}
