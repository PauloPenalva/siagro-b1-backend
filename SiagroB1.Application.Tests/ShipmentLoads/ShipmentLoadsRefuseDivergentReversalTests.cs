using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1226: estorno das recusas de documento vinculado a NF do SAP com divergência. O ajuste do efetivo fica no
/// romaneio da recusa que COMPLETOU a devolução; estornar uma recusa anterior primeiro deixaria a diferença presa nele
/// com o termo de divergência de volta — saldo negativo. Por isso o estorno é em ordem inversa, só para documento
/// divergente.
/// </summary>
/// <remarks>Reaproveita a fixture de <see cref="ShipmentLoadsRefuseServiceTests"/> (faturamento pelo caminho REAL).</remarks>
public class ShipmentLoadsRefuseDivergentReversalTests
{
    private readonly ShipmentLoadsRefuseServiceTests _fixture = new();

    private SalesInvoicesReverseConfirmService Reverse() =>
        new(_fixture._db,
            new SalesContractsAllocationDeleteForInvoiceService(_fixture._db),
            new ShipmentReleasesRecalculateShippedService(_fixture._db.Context),
            new ShipmentLoadsBalanceHookService(
                _fixture._db.Context, new ShipmentLoadsMovementLogService(_fixture._db.Context)),
            new ShipmentLoadsClosureHookService(
                _fixture._db.Context, new ShipmentLoadsChangeLogService(_fixture._db.Context)),
            new FakeStringLocalizer<Resource>());

    private async Task RefuseToWarehouseAsync(ShipmentLoad load, SalesInvoice invoice, decimal quantity)
    {
        await _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, quantity, RefusalDestination.Warehouse, ShipmentLoadsRefuseServiceTests.DestinationWarehouse),
            "tester");
        _fixture._db.Context.ChangeTracker.Clear();
    }

    /// <summary>As devoluções, da mais antiga para a mais recente.</summary>
    private Task<List<SalesInvoice>> ReturnsAsync() =>
        _fixture._db.Context.SalesInvoices.AsNoTracking()
            .Where(x => x.InvoiceType == SalesInvoiceType.Return)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

    private async Task ReverseAsync(Guid returnKey)
    {
        await Reverse().ExecuteAsync(returnKey, "tester");
        _fixture._db.Context.ChangeTracker.Clear();
    }

    /// <summary>10.000 e depois 19.800 devolvem o documento divergente inteiro: a primeira recusa não se estorna antes.</summary>
    [Fact]
    public async Task Reversing_the_earlier_of_two_divergent_refusals_is_refused()
    {
        var (load, invoice) = await _fixture.DivergentBilledLoadAsync();
        await RefuseToWarehouseAsync(load, invoice, 10_000m);
        await RefuseToWarehouseAsync(load, invoice, 19_800m);
        var before = await _fixture.LoadAsync(load.Key);
        var returns = await ReturnsAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Reverse().ExecuteAsync(returns[0].Key, "tester"));
        _fixture._db.Context.ChangeTracker.Clear();

        Assert.Equal("Estorne primeiro a recusa mais recente deste documento.", ex.Message);
        Assert.All(await ReturnsAsync(), r => Assert.Equal(InvoiceStatus.Confirmed, r.InvoiceStatus));
        Assert.Equal(2, await _fixture._db.Context.StorageTransactions.AsNoTracking().CountAsync(x =>
            x.TransactionType == StorageTransactionType.SalesShipmentReturn &&
            x.TransactionStatus == StorageTransactionsStatus.Confirmed));
        var after = await _fixture.LoadAsync(load.Key);
        Assert.Equal(before.ReturnedToWarehouseQuantity, after.ReturnedToWarehouseQuantity);
        Assert.Equal(before.InvoicedQuantity, after.InvoicedQuantity);
        Assert.Equal(before.AvailableQuantity, after.AvailableQuantity);
    }

    /// <summary>
    /// Em ordem inversa o saldo fecha a cada passo: estornada a mais recente (com o ajuste), a carga volta ao estado da
    /// primeira recusa parcial; depois a primeira também se estorna.
    /// </summary>
    [Fact]
    public async Task Reversing_divergent_refusals_newest_first_keeps_the_balance_closed()
    {
        var (load, invoice) = await _fixture.DivergentBilledLoadAsync();
        await RefuseToWarehouseAsync(load, invoice, 10_000m);
        await RefuseToWarehouseAsync(load, invoice, 19_800m);
        var returns = await ReturnsAsync();

        await ReverseAsync(returns[1].Key);

        var afterSecond = await _fixture.LoadAsync(load.Key);
        Assert.Equal(10_000m, afterSecond.ReturnedToWarehouseQuantity);
        Assert.Equal(19_800m, afterSecond.InvoicedQuantity);
        Assert.Equal(200m, afterSecond.InvoiceDivergenceQuantity);
        Assert.Equal(decimal.Zero, afterSecond.AvailableQuantity);

        // A devolução estornada volta a Pending e não segura mais o ajuste: a anterior agora se estorna.
        await ReverseAsync(returns[0].Key);

        var afterFirst = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, afterFirst.ReturnedToWarehouseQuantity);
        Assert.Equal(29_800m, afterFirst.InvoicedQuantity);
        Assert.Equal(200m, afterFirst.InvoiceDivergenceQuantity);
        Assert.Equal(decimal.Zero, afterFirst.AvailableQuantity);
    }

    /// <summary>A simetria da recusa total: estornada, a carga volta a 29.800 faturados, 200 de divergência, saldo zero.</summary>
    [Fact]
    public async Task Reversing_a_single_full_divergent_refusal_restores_the_load()
    {
        var (load, invoice) = await _fixture.DivergentBilledLoadAsync();
        await RefuseToWarehouseAsync(load, invoice, 29_800m);

        await ReverseAsync((await ReturnsAsync()).Single().Key);

        var restored = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, restored.ReturnedToWarehouseQuantity);
        Assert.Equal(29_800m, restored.InvoicedQuantity);
        Assert.Equal(200m, restored.InvoiceDivergenceQuantity);
        Assert.Equal(decimal.Zero, restored.AvailableQuantity);
    }

    /// <summary>Sem divergência, nada muda: as recusas parciais se estornam em qualquer ordem.</summary>
    [Fact]
    public async Task Non_divergent_refusals_can_be_reversed_in_any_order()
    {
        var (load, invoice) = await _fixture.BilledLoadAsync(30_000m);
        _fixture._db.Context.ChangeTracker.Clear();
        await RefuseToWarehouseAsync(load, invoice, 10_000m);
        await RefuseToWarehouseAsync(load, invoice, 20_000m);
        var returns = await ReturnsAsync();

        await ReverseAsync(returns[0].Key);

        var after = await _fixture.LoadAsync(load.Key);
        Assert.Equal(20_000m, after.ReturnedToWarehouseQuantity);
        Assert.Equal(10_000m, after.InvoicedQuantity);
        Assert.Equal(decimal.Zero, after.AvailableQuantity);
    }
}
