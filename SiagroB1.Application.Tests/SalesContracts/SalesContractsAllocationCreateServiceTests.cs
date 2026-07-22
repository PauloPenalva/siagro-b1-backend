using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsAllocationCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsAllocationCreateService Service() => new(_db);

    [Fact]
    public async Task ExecuteForInvoice_CreatesDefaultAllocation_WithSnapshotsAndBalances()
    {
        var contract = NewContract(totalVolume: 1000m, price: 100m);
        var release = NewRelease(contract.Key, released: 500m);
        var invoice = NewInvoice();
        var item = NewItem(invoice, contract.Key, release.Key, quantity: 200m, unitPrice: 90m);
        _db.Context.AddRange(contract, release, invoice);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteForInvoiceAsync(invoice, "tester");

        var allocation = await _db.Context.SalesContractsAllocations.AsNoTracking().SingleAsync();
        Assert.Equal(contract.Key, allocation.SalesContractKey);
        Assert.Equal(item.Key!.Value, allocation.SalesInvoiceItemKey);
        Assert.Equal(release.Key, allocation.SalesShipmentReleaseKey);
        Assert.Equal(200m, allocation.Volume);
        Assert.Equal(90m, allocation.InvoiceUnitPrice);
        Assert.Equal(100m, allocation.ContractPrice);
        Assert.Equal(-2000m, allocation.PriceDifference); // 200 × (90 − 100): NF < contrato → negativo
        Assert.Equal(SalesContractAllocationOrigin.Billing, allocation.Origin);
        Assert.Equal("tester", allocation.ApprovedBy);

        Assert.Equal(200m, (await ContractAsync(_db, contract.Key)).AllocatedVolume);
        Assert.Equal(800m, (await ContractAsync(_db, contract.Key)).AvaiableVolume);
    }

    [Fact]
    public async Task ExecuteForInvoice_IsIdempotentPerItem()
    {
        var contract = NewContract(totalVolume: 1000m);
        var invoice = NewInvoice();
        NewItem(invoice, contract.Key, null, quantity: 200m);
        _db.Context.AddRange(contract, invoice);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteForInvoiceAsync(invoice, "tester");
        await Service().ExecuteForInvoiceAsync(invoice, "tester"); // reconfirmação

        Assert.Equal(1, await _db.Context.SalesContractsAllocations.CountAsync());
        Assert.Equal(200m, (await ContractAsync(_db, contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task ExecuteForInvoice_SelfHealsCorruptedAllocatedVolume()
    {
        var contract = NewContract(totalVolume: 1000m);
        contract.AllocatedVolume = 555m; // corrompido
        var invoice = NewInvoice();
        NewItem(invoice, contract.Key, null, quantity: 200m);
        _db.Context.AddRange(contract, invoice);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteForInvoiceAsync(invoice, "tester");

        // Derivado-da-soma: ignora o valor corrompido e grava só o que o ledger sustenta.
        Assert.Equal(200m, (await ContractAsync(_db, contract.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task ExecuteForInvoice_FinishedContract_Throws()
    {
        var contract = NewContract(totalVolume: 1000m, status: ContractStatus.Finished);
        var invoice = NewInvoice();
        NewItem(invoice, contract.Key, null, quantity: 200m);
        _db.Context.AddRange(contract, invoice);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteForInvoiceAsync(invoice, "tester"));
        Assert.Contains("encerrado", ex.Message);
    }

    [Fact]
    public async Task ExecuteForInvoice_ItemsWithoutContract_AreSkipped()
    {
        var contract = NewContract(totalVolume: 1000m);
        var invoice = NewInvoice();
        NewItem(invoice, contract.Key, null, quantity: 200m);
        NewItem(invoice, null, null, quantity: 300m); // sem contrato — fluxo avulso
        _db.Context.AddRange(contract, invoice);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteForInvoiceAsync(invoice, "tester");

        Assert.Equal(1, await _db.Context.SalesContractsAllocations.CountAsync());
    }
}
