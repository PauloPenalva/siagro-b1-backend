using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsAllocationDeleteForInvoiceServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsAllocationDeleteForInvoiceService Service() => new(_db);

    [Fact]
    public async Task Execute_DeletesAllInvoiceAllocations_IncludingReallocationPairs_AndRestoresBalances()
    {
        // Item de 200 em A com 80 realocados para B; cancelar a nota derruba TUDO.
        var a = NewContract(totalVolume: 1000m);
        var b = NewContract(totalVolume: 1000m, price: 120m);
        var releaseA = NewRelease(a.Key, released: 500m, shipped: 120m);
        var releaseB = NewRelease(b.Key, released: 500m, shipped: 80m);
        var invoice = NewInvoice();
        var item = NewItem(invoice, a.Key, releaseA.Key, quantity: 200m);
        a.AllocatedVolume = 120m;
        b.AllocatedVolume = 80m;

        var group = Guid.NewGuid();
        _db.Context.AddRange(a, b, releaseA, releaseB, invoice);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(a.Key, item.Key!.Value, 200m, releaseA.Key),
            NewAllocation(a.Key, item.Key!.Value, -80m, releaseA.Key,
                origin: SalesContractAllocationOrigin.Reallocation, groupKey: group),
            NewAllocation(b.Key, item.Key!.Value, 80m, releaseB.Key,
                origin: SalesContractAllocationOrigin.Reallocation, contractPrice: 120m, groupKey: group));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(0, await _db.Context.SalesContractsAllocations.CountAsync());
        Assert.Equal(0m, (await ContractAsync(_db, a.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractAsync(_db, b.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseAsync(_db, releaseA.Key)).ShippedQuantity);
        Assert.Equal(0m, (await ReleaseAsync(_db, releaseB.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Execute_KeepsAllocationsOfOtherInvoices()
    {
        var contract = NewContract(totalVolume: 1000m);
        var invoice = NewInvoice();
        var item = NewItem(invoice, contract.Key, null, quantity: 200m);
        var otherInvoice = NewInvoice();
        var otherItem = NewItem(otherInvoice, contract.Key, null, quantity: 50m);
        contract.AllocatedVolume = 250m;
        _db.Context.AddRange(contract, invoice, otherInvoice);
        _db.Context.SalesContractsAllocations.AddRange(
            NewAllocation(contract.Key, item.Key!.Value, 200m),
            NewAllocation(contract.Key, otherItem.Key!.Value, 50m));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(1, await _db.Context.SalesContractsAllocations.CountAsync());
        Assert.Equal(50m, (await ContractAsync(_db, contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Execute_DeferredCommitMode_LeavesPersistenceToCaller()
    {
        var contract = NewContract(totalVolume: 1000m);
        var invoice = NewInvoice();
        var item = NewItem(invoice, contract.Key, null, quantity: 200m);
        contract.AllocatedVolume = 200m;
        _db.Context.AddRange(contract, invoice);
        _db.Context.SalesContractsAllocations.Add(
            NewAllocation(contract.Key, item.Key!.Value, 200m));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(invoice.Key, "tester", CommitMode.Deferred);
        await _db.SaveChangesAsync(); // o chamador persiste (transação do fluxo)

        Assert.Equal(0, await _db.Context.SalesContractsAllocations.CountAsync());
        Assert.Equal(0m, (await ContractAsync(_db, contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Execute_InvoiceWithoutAllocations_IsNoOp()
    {
        var invoice = NewInvoice(status: InvoiceStatus.Pending);
        _db.Context.Add(invoice);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(0, await _db.Context.SalesContractsAllocations.CountAsync());
    }
}
