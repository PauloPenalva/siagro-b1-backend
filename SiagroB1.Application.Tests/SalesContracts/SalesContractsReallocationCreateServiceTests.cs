using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsReallocationCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsReallocationCreateService Service() =>
        new(_db, new SalesShipmentReleaseMovementGuardService(_db.Context));

    private SalesContract _source = null!;
    private SalesContract _target = null!;
    private SalesShipmentRelease _sourceRelease = null!;
    private SalesShipmentRelease _targetRelease = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;

    /// <summary>
    /// Cenário base: item de 200 faturado no contrato de origem (preço 100, NF 90),
    /// liberação de origem consumida em 200; destino com preço 120 e liberação ativa de 500.
    /// </summary>
    private async Task SeedAsync(decimal targetPrice = 120m)
    {
        _source = NewContract(totalVolume: 1000m, price: 100m);
        _target = NewContract(totalVolume: 1000m, price: targetPrice);
        _sourceRelease = NewRelease(_source.Key, released: 500m, shipped: 200m);
        _targetRelease = NewRelease(_target.Key, released: 500m);
        _invoice = NewInvoice();
        _item = NewItem(_invoice, _source.Key, _sourceRelease.Key, quantity: 200m, unitPrice: 90m);

        _source.AllocatedVolume = 200m;

        _db.Context.AddRange(_source, _target, _sourceRelease, _targetRelease, _invoice);
        _db.Context.SalesContractsAllocations.Add(NewAllocation(
            _source.Key, _item.Key!.Value, 200m, _sourceRelease.Key,
            invoiceUnitPrice: 90m, contractPrice: 100m));
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Execute_CreatesPair_MovesBalances_AndComputesPriceDifference()
    {
        await SeedAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            _targetRelease.Key, 80m, "tester");

        var rows = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(a => a.Origin == SalesContractAllocationOrigin.Reallocation)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.ReallocationGroupKey));
        Assert.Equal(rows[0].ReallocationGroupKey, rows[1].ReallocationGroupKey);

        var negative = rows.Single(r => r.Volume < 0);
        Assert.Equal(-80m, negative.Volume);
        Assert.Equal(_source.Key, negative.SalesContractKey);
        Assert.Equal(_sourceRelease.Key, negative.SalesShipmentReleaseKey);
        Assert.Equal(100m, negative.ContractPrice);
        Assert.Equal(800m, negative.PriceDifference); // -80 × (90 − 100), reverte a diferença da origem
        Assert.Equal(_target.Key, negative.CounterpartySalesContractKey); // rastreio: foi para o destino

        var positive = rows.Single(r => r.Volume > 0);
        Assert.Equal(80m, positive.Volume);
        Assert.Equal(_target.Key, positive.SalesContractKey);
        Assert.Equal(_targetRelease.Key, positive.SalesShipmentReleaseKey);
        Assert.Equal(120m, positive.ContractPrice);
        Assert.Equal(-2400m, positive.PriceDifference); // 80 × (90 − 120): NF < contrato destino → negativo
        Assert.Equal(_source.Key, positive.CounterpartySalesContractKey); // rastreio: veio da origem

        Assert.Equal(120m, (await ContractAsync(_db, _source.Key)).AllocatedVolume);
        Assert.Equal(80m, (await ContractAsync(_db, _target.Key)).AllocatedVolume);
        Assert.Equal(120m, (await ReleaseAsync(_db, _sourceRelease.Key)).ShippedQuantity);
        Assert.Equal(80m, (await ReleaseAsync(_db, _targetRelease.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Execute_SelfHealsCorruptedBalances()
    {
        await SeedAsync();
        var tracked = await _db.Context.SalesContracts.SingleAsync(c => c.Key == _source.Key);
        tracked.AllocatedVolume = 999m; // corrompido
        var trackedRelease = await _db.Context.SalesShipmentReleases.SingleAsync(r => r.Key == _sourceRelease.Key);
        trackedRelease.ShippedQuantity = 999m; // corrompido
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            _targetRelease.Key, 80m, "tester");

        Assert.Equal(120m, (await ContractAsync(_db, _source.Key)).AllocatedVolume);
        Assert.Equal(120m, (await ReleaseAsync(_db, _sourceRelease.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Execute_PendingInvoice_Throws()
    {
        await SeedAsync();
        var invoice = await _db.Context.SalesInvoices.SingleAsync(i => i.Key == _invoice.Key);
        invoice.InvoiceStatus = InvoiceStatus.Pending;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
        Assert.Contains("Somente notas confirmadas", ex.Message);
    }

    [Fact]
    public async Task Execute_ReturnInvoice_Throws()
    {
        await SeedAsync();
        var invoice = await _db.Context.SalesInvoices.SingleAsync(i => i.Key == _invoice.Key);
        invoice.InvoiceType = SalesInvoiceType.Return;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
    }

    [Fact]
    public async Task Execute_FinishedContract_Throws()
    {
        await SeedAsync();
        var target = await _db.Context.SalesContracts.SingleAsync(c => c.Key == _target.Key);
        target.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
        Assert.Contains("encerrado", ex.Message);
    }

    /// <summary>
    /// Contrato de outro cliente é destino VÁLIDO: a conferência com o relatório de
    /// entrega revela notas que pertencem ao contrato de outra empresa, e essa é a única
    /// saída para corrigi-las.
    /// </summary>
    [Fact]
    public async Task Execute_DifferentCustomer_CreatesPair()
    {
        await SeedAsync();
        var target = await _db.Context.SalesContracts.SingleAsync(c => c.Key == _target.Key);
        target.CardCode = "OUTRO";
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            _targetRelease.Key, 80m, "tester");

        var rows = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(a => a.Origin == SalesContractAllocationOrigin.Reallocation)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0m, rows.Sum(r => r.Volume)); // o par −/+ se anula no ledger
        Assert.Equal(120m, (await ContractAsync(_db, _source.Key)).AllocatedVolume);
        Assert.Equal(80m, (await ContractAsync(_db, _target.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Execute_DifferentProductOrUom_Throws()
    {
        await SeedAsync();
        var target = await _db.Context.SalesContracts.SingleAsync(c => c.Key == _target.Key);
        target.ItemCode = "MILHO";
        await _db.Context.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
        Assert.Contains("outro produto", ex.Message);

        target.ItemCode = "SOJA";
        target.UnitOfMeasureCode = "SC";
        await _db.Context.SaveChangesAsync();
        ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
        Assert.Contains("unidade de medida", ex.Message);
    }

    [Fact]
    public async Task Execute_VolumeAboveSourceAllocatedBalance_Throws()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 201m, "tester"));
        Assert.Contains("superior ao saldo alocado", ex.Message);
    }

    [Fact]
    public async Task Execute_SourceBalanceConsidersPreviousReturnsAndReallocations()
    {
        await SeedAsync();

        // Devolução anterior de 150 (linha negativa em item de devolução apontando p/ origem).
        var returnInvoice = NewInvoice(type: SalesInvoiceType.Return, originKey: _invoice.Key);
        var returnItem = NewItem(returnInvoice, _source.Key, null, quantity: 150m,
            originItemKey: _item.Key);
        _db.Context.Add(returnInvoice);
        _db.Context.SalesContractsAllocations.Add(NewAllocation(
            _source.Key, returnItem.Key!.Value, -150m, _sourceRelease.Key,
            origin: SalesContractAllocationOrigin.Return));
        await _db.Context.SaveChangesAsync();

        // Restam 50 na origem: 80 deve falhar, 50 deve passar.
        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            _targetRelease.Key, 50m, "tester");
        Assert.Equal(50m, (await ContractAsync(_db, _target.Key)).AllocatedVolume);
    }

    [Theory]
    [InlineData(ReleaseStatus.Pending)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Completed)]
    public async Task Execute_TargetReleaseNotActive_Throws(ReleaseStatus status)
    {
        await SeedAsync();
        var release = await _db.Context.SalesShipmentReleases.SingleAsync(r => r.Key == _targetRelease.Key);
        release.Status = status;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
    }

    [Fact]
    public async Task Execute_TargetReleaseWithoutBalance_Throws()
    {
        await SeedAsync();
        var release = await _db.Context.SalesShipmentReleases.SingleAsync(r => r.Key == _targetRelease.Key);
        release.ShippedQuantity = 450m; // saldo 50 < 80
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
        Assert.Contains("liberação de destino insuficiente", ex.Message);
    }

    [Fact]
    public async Task Execute_TargetReleaseOfAnotherContract_Throws()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _sourceRelease.Key, 80m, "tester"));
        Assert.Contains("não pertence ao contrato de destino", ex.Message);
    }

    [Fact]
    public async Task Execute_TargetContractWithoutBalance_Throws()
    {
        await SeedAsync();
        var target = await _db.Context.SalesContracts.SingleAsync(c => c.Key == _target.Key);
        target.AllocatedVolume = 950m; // saldo 50 < 80
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
        Assert.Contains("contrato de destino insuficiente", ex.Message);
    }

    [Fact]
    public async Task Execute_InvalidVolumeOrSameContract_Throws()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, _targetRelease.Key, 0m, "tester"));
        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _source.Key, _sourceRelease.Key, 80m, "tester"));
    }

    [Fact]
    public async Task ExecuteWithTransaction_WrapsErrorsInDefaultException()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Service().ExecuteWithTransactionAsync(
            Guid.NewGuid(), _source.Key, _target.Key, _targetRelease.Key, 80m, "tester"));
    }
}
