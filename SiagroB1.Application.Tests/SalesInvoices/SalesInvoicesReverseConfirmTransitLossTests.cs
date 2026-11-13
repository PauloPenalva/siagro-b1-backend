using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Estorno da confirmação da devolução nascida de uma recusa com Quebra de trajeto (GAC-1213):
/// o romaneio 18 daquela recusa é cancelado e a carga volta ao que era antes da quebra.
/// </summary>
/// <remarks>
/// Monta e recusa a carga pelo caminho REAL, com a fixture de
/// <see cref="ShipmentLoadsRefuseServiceTests"/>.
/// </remarks>
public class SalesInvoicesReverseConfirmTransitLossTests
{
    private readonly ShipmentLoadsRefuseServiceTests _fixture = new();

    private SalesInvoicesReverseConfirmService Reverse()
    {
        var db = _fixture._db;
        return new(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>());
    }

    /// <summary>Carga faturada em uma nota por quantidade informada.</summary>
    private async Task<(ShipmentLoad Load, SalesContract Contract, List<SalesInvoice> Invoices)>
        BilledLoadAsync(params decimal[] quantities)
    {
        var (load, contract, release, _) = await _fixture.SeedAsync(quantities.Sum());
        var invoices = new List<SalesInvoice>();
        foreach (var quantity in quantities)
        {
            var invoice = ShipmentLoadsRefuseServiceTests.InvoiceFor(load, contract, release, quantity);
            await _fixture.BillingService().ExecuteAsync(invoice, "tester");
            invoices.Add(invoice);
        }

        // A numeração automática do InMemory pode repetir o número entre notas; a seleção da
        // entrada da recusa é pelo número da origem, então cada nota precisa do seu.
        for (var i = 0; i < invoices.Count; i++)
            invoices[i].InvoiceNumber = $"00000{i + 1}001";
        await _fixture._db.SaveChangesAsync();

        return (load, contract, invoices);
    }

    private Task RefuseAsync(
        ShipmentLoad load, RefusalDestination destination, params (SalesInvoice Invoice, decimal Quantity)[] lines) =>
        _fixture.Service().ExecuteAsync(
            new RefusalRequest(load.Key,
                lines.Select(l => new RefusalLine(l.Invoice.Key, l.Quantity)).ToList(),
                destination,
                null,
                "Carga roubada na BR-116",
                destination == RefusalDestination.TransitLoss ? TransitLossReason.Theft : null),
            "tester");

    private Task<SalesInvoice> ReturnOfAsync(SalesInvoice origin) =>
        _fixture._db.Context.SalesInvoices.AsNoTracking()
            .SingleAsync(x => x.InvoiceType == SalesInvoiceType.Return && x.SalesInvoiceOriginKey == origin.Key);

    private Task<List<StorageTransaction>> TransitLossEntriesAsync() =>
        _fixture._db.Context.StorageTransactions.AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.TransitLoss)
            .ToListAsync();

    private Task<InvoiceStatus?> StatusOfAsync(Guid key) =>
        _fixture._db.Context.SalesInvoices.AsNoTracking()
            .Where(x => x.Key == key).Select(x => x.InvoiceStatus).SingleAsync();

    [Fact]
    public async Task Reversing_the_return_cancels_the_transit_loss_and_restores_the_load()
    {
        var (load, contract, invoices) = await BilledLoadAsync(40_000m);
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[0], 40_000m));
        var returnInvoice = await ReturnOfAsync(invoices[0]);

        await Reverse().ExecuteAsync(returnInvoice.Key, "tester");

        var entry = Assert.Single(await TransitLossEntriesAsync());
        Assert.Equal(StorageTransactionsStatus.Cancelled, entry.TransactionStatus);
        Assert.Equal("tester", entry.CanceledBy);
        Assert.NotNull(entry.CanceledAt);

        var reloaded = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, reloaded.TransitLossQuantity);
        Assert.Equal(40_000m, reloaded.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, reloaded.Status);

        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(returnInvoice.Key));
        Assert.Equal(40_000m, (await SalesContractsAllocationTestSupport
            .ContractAsync(_fixture._db, contract.Key)).AllocatedVolume);
    }

    /// <summary>
    /// A quebra desfeita aparece no histórico da carga: movimento positivo com o volume do
    /// romaneio cancelado, um por romaneio (não por devolução irmã), com saldo já recalculado.
    /// </summary>
    [Fact]
    public async Task Reversing_the_loss_registers_one_positive_transit_loss_movement_per_entry()
    {
        var (load, _, invoices) = await BilledLoadAsync(30_000m, 10_000m);
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[0], 30_000m), (invoices[1], 10_000m));
        var first = await ReturnOfAsync(invoices[0]);
        var entry = Assert.Single(await TransitLossEntriesAsync());

        await Reverse().ExecuteAsync(first.Key, "tester");

        var movements = await _fixture._db.Context.ShipmentLoadMovements.AsNoTracking()
            .Where(x => x.ShipmentLoadKey == load.Key && x.MovementType == ShipmentLoadMovementType.TransitLoss)
            .OrderBy(x => x.Quantity)
            .ToListAsync();
        Assert.Equal(2, movements.Count);

        var undone = movements.Last();
        Assert.Equal(40_000m, undone.Quantity);
        Assert.Equal(entry.Key, undone.StorageTransactionKey);
        Assert.Equal((await _fixture.LoadAsync(load.Key)).AvailableQuantity, undone.BalanceAfter);
        Assert.Contains("Quebra de trajeto desfeita", undone.Description);
        Assert.Contains(entry.Code!, undone.Description);
        Assert.Contains(first.InvoiceNumber!, undone.Description);
    }

    [Fact]
    public async Task Reversing_one_return_of_a_two_document_loss_reverses_the_sibling_too()
    {
        var (load, _, invoices) = await BilledLoadAsync(30_000m, 10_000m);
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[0], 30_000m), (invoices[1], 10_000m));
        var first = await ReturnOfAsync(invoices[0]);
        var second = await ReturnOfAsync(invoices[1]);

        await Reverse().ExecuteAsync(first.Key, "tester");

        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(first.Key));
        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(second.Key));
        Assert.Equal(StorageTransactionsStatus.Cancelled, Assert.Single(await TransitLossEntriesAsync()).TransactionStatus);

        var reloaded = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, reloaded.TransitLossQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, reloaded.Status);
    }

    [Fact]
    public async Task Reversing_the_second_loss_cancels_only_its_own_entry()
    {
        var (load, _, invoices) = await BilledLoadAsync(30_000m, 10_000m);
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[0], 15_000m));
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[1], 10_000m));
        var firstReturn = await ReturnOfAsync(invoices[0]);
        var secondReturn = await ReturnOfAsync(invoices[1]);

        await Reverse().ExecuteAsync(secondReturn.Key, "tester");

        var entries = await TransitLossEntriesAsync();
        Assert.Equal(2, entries.Count);
        var firstEntry = entries.Single(e => RefusalEntryDocuments.Parse(e.Comments)!.Contains(invoices[0].InvoiceNumber!));
        var secondEntry = entries.Single(e => RefusalEntryDocuments.Parse(e.Comments)!.Contains(invoices[1].InvoiceNumber!));
        Assert.Equal(StorageTransactionsStatus.Confirmed, firstEntry.TransactionStatus);
        Assert.Equal(StorageTransactionsStatus.Cancelled, secondEntry.TransactionStatus);

        Assert.Equal(InvoiceStatus.Confirmed, await StatusOfAsync(firstReturn.Key));
        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(secondReturn.Key));
        Assert.Equal(15_000m, (await _fixture.LoadAsync(load.Key)).TransitLossQuantity);
    }

    private Task RefuseToWarehouseAsync(ShipmentLoad load, SalesInvoice invoice, decimal quantity) =>
        _fixture.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(
                load, invoice, quantity, RefusalDestination.Warehouse,
                ShipmentLoadsRefuseServiceTests.DestinationWarehouse),
            "tester");

    private Task<StorageTransaction> EntryAsync(StorageTransactionType type) =>
        _fixture._db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.TransactionType == type && x.RefusedFromShipmentLoadKey != null);

    private Task<SalesInvoice> ReturnLinkedToAsync(StorageTransaction entry) =>
        _fixture._db.Context.SalesInvoices.AsNoTracking()
            .SingleAsync(x => x.RefusalStorageTransactionKey == entry.Key);

    /// <summary>
    /// Mesmo documento recusado em parcelas para destinos diferentes (Review Focus 2): 15 t de
    /// quebra e 25 t ao armazém. As duas entradas listam o mesmo número; só o vínculo as separa.
    /// </summary>
    private async Task<(ShipmentLoad Load, StorageTransaction Loss, StorageTransaction Warehouse,
        SalesInvoice LossReturn, SalesInvoice WarehouseReturn)> SameInvoiceLossThenWarehouseAsync()
    {
        var (load, _, invoices) = await BilledLoadAsync(40_000m);
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[0], 15_000m));
        await RefuseToWarehouseAsync(load, invoices[0], 25_000m);

        var loss = await EntryAsync(StorageTransactionType.TransitLoss);
        var warehouse = await EntryAsync(StorageTransactionType.SalesShipmentReturn);
        return (load, loss, warehouse, await ReturnLinkedToAsync(loss), await ReturnLinkedToAsync(warehouse));
    }

    private Task<List<ShipmentRelease>> ReturnReleasesAsync(StorageTransaction entry) =>
        _fixture._db.Context.ShipmentReleases.AsNoTracking()
            .Where(x => x.GeneratedByStorageTransactionKey == entry.Key).ToListAsync();

    [Fact]
    public async Task Same_invoice_reversing_the_loss_return_leaves_the_warehouse_refusal_alone()
    {
        var (load, loss, warehouse, lossReturn, warehouseReturn) = await SameInvoiceLossThenWarehouseAsync();
        var balanceBefore = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _fixture._db.Context, warehouse.WarehouseCode, warehouse.ItemCode);
        var releasesBefore = (await ReturnReleasesAsync(warehouse)).Select(r => r.Status).ToList();

        await Reverse().ExecuteAsync(lossReturn.Key, "tester");

        Assert.Equal(StorageTransactionsStatus.Cancelled,
            (await EntryAsync(StorageTransactionType.TransitLoss)).TransactionStatus);
        Assert.Equal(StorageTransactionsStatus.Confirmed,
            (await EntryAsync(StorageTransactionType.SalesShipmentReturn)).TransactionStatus);
        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(lossReturn.Key));
        Assert.Equal(InvoiceStatus.Confirmed, await StatusOfAsync(warehouseReturn.Key));
        Assert.Equal(balanceBefore, await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _fixture._db.Context, warehouse.WarehouseCode, warehouse.ItemCode));
        Assert.Equal(releasesBefore, (await ReturnReleasesAsync(warehouse)).Select(r => r.Status).ToList());

        var reloaded = await _fixture.LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, reloaded.TransitLossQuantity);
        Assert.Equal(25_000m, reloaded.ReturnedToWarehouseQuantity);
    }

    [Fact]
    public async Task Same_invoice_reversing_the_warehouse_return_leaves_the_loss_alone()
    {
        var (load, _, _, lossReturn, warehouseReturn) = await SameInvoiceLossThenWarehouseAsync();

        await Reverse().ExecuteAsync(warehouseReturn.Key, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed,
            (await EntryAsync(StorageTransactionType.TransitLoss)).TransactionStatus);
        Assert.Equal(StorageTransactionsStatus.Cancelled,
            (await EntryAsync(StorageTransactionType.SalesShipmentReturn)).TransactionStatus);
        Assert.Equal(InvoiceStatus.Confirmed, await StatusOfAsync(lossReturn.Key));
        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(warehouseReturn.Key));
        Assert.Equal(15_000m, (await _fixture.LoadAsync(load.Key)).TransitLossQuantity);
    }

    [Fact]
    public async Task Sync_refusals_link_every_return_to_the_entry_and_rebilling_links_none()
    {
        var (load, _, invoices) = await BilledLoadAsync(20_000m, 10_000m, 10_000m);
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[0], 5_000m), (invoices[1], 5_000m));
        await _fixture.Service().ExecuteAsync(
            new RefusalRequest(load.Key,
                [new RefusalLine(invoices[0].Key, 5_000m), new RefusalLine(invoices[2].Key, 5_000m)],
                RefusalDestination.Warehouse, ShipmentLoadsRefuseServiceTests.DestinationWarehouse, "Qualidade"),
            "tester");
        await RefuseAsync(load, RefusalDestination.Rebilling, (invoices[1], 5_000m));

        var loss = await EntryAsync(StorageTransactionType.TransitLoss);
        var warehouse = await EntryAsync(StorageTransactionType.SalesShipmentReturn);
        var returns = await _fixture._db.Context.SalesInvoices.AsNoTracking()
            .Where(x => x.InvoiceType == SalesInvoiceType.Return).ToListAsync();

        Assert.Equal(2, returns.Count(r => r.RefusalStorageTransactionKey == loss.Key));
        Assert.Equal(2, returns.Count(r => r.RefusalStorageTransactionKey == warehouse.Key));
        Assert.Single(returns, r => r.RefusalStorageTransactionKey == null);
    }

    /// <summary>Devolução anterior ao vínculo: a entrada de armazém ainda é achada pela lista do Comments.</summary>
    [Fact]
    public async Task Legacy_warehouse_return_without_link_still_reverses_through_the_comments()
    {
        var (load, _, invoices) = await BilledLoadAsync(40_000m);
        await RefuseToWarehouseAsync(load, invoices[0], 40_000m);
        var returnInvoice = await ReturnOfAsync(invoices[0]);

        var tracked = await _fixture._db.Context.SalesInvoices.SingleAsync(x => x.Key == returnInvoice.Key);
        tracked.RefusalStorageTransactionKey = null;
        await _fixture._db.SaveChangesAsync();
        _fixture._db.Context.ChangeTracker.Clear();

        await Reverse().ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(StorageTransactionsStatus.Cancelled,
            (await EntryAsync(StorageTransactionType.SalesShipmentReturn)).TransactionStatus);
        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(returnInvoice.Key));
    }

    [Fact]
    public async Task Reversing_a_rebilling_return_does_not_touch_the_transit_loss_entry()
    {
        var (load, _, invoices) = await BilledLoadAsync(30_000m, 10_000m);
        await RefuseAsync(load, RefusalDestination.TransitLoss, (invoices[0], 15_000m));
        await RefuseAsync(load, RefusalDestination.Rebilling, (invoices[1], 10_000m));
        var lossReturn = await ReturnOfAsync(invoices[0]);
        var rebillingReturn = await ReturnOfAsync(invoices[1]);

        await Reverse().ExecuteAsync(rebillingReturn.Key, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, Assert.Single(await TransitLossEntriesAsync()).TransactionStatus);
        Assert.Equal(InvoiceStatus.Confirmed, await StatusOfAsync(lossReturn.Key));
        Assert.Equal(InvoiceStatus.Pending, await StatusOfAsync(rebillingReturn.Key));
        Assert.Equal(15_000m, (await _fixture.LoadAsync(load.Key)).TransitLossQuantity);
    }

    /// <summary>
    /// GAC-1214: estornar a devolução de uma recusa CONFIRMADA ao armazém (ReclaimGoodsFromWarehouse →
    /// CancelReturnReleasesAsync) cancela o romaneio 12 e a liberação; a alocação negativa do 12 some
    /// e o saldo do contrato volta ao valor anterior à recusa.
    /// </summary>
    [Fact]
    public async Task Reversing_a_confirmed_warehouse_refusal_takes_the_contract_balance_back()
    {
        var (load, _, invoices) = await BilledLoadAsync(40_000m);
        var contract = await _fixture.SeedPurchaseOriginAsync(await _fixture.LoadShipmentKeyAsync(load));

        // O grão saiu do contrato na compra: 40.000 alocados.
        var shipment = await _fixture._db.Context.StorageTransactions.AsNoTracking()
            .FirstAsync(x => x.TransactionType == StorageTransactionType.SalesShipment);
        _fixture._db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            PurchaseContractKey = contract.Key, StorageTransactionKey = shipment.Key, Volume = 40_000m,
            ApprovedAt = DateTime.Now, ApprovedBy = "tester",
        });
        (await _fixture._db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key)).AllocatedVolume = 40_000m;
        await _fixture._db.SaveChangesAsync();

        await RefuseToWarehouseAsync(load, invoices[0], 15_000m);
        var entry = await EntryAsync(StorageTransactionType.SalesShipmentReturn);
        Assert.Equal(25_000m,
            (await _fixture._db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).AllocatedVolume);
        Assert.True(await _fixture._db.Context.PurchaseContractsAllocations.AsNoTracking()
            .AnyAsync(x => x.StorageTransactionKey == entry.Key));

        await Reverse().ExecuteAsync((await ReturnLinkedToAsync(entry)).Key, "tester");

        Assert.Equal(StorageTransactionsStatus.Cancelled, (await EntryAsync(StorageTransactionType.SalesShipmentReturn)).TransactionStatus);
        Assert.All(await ReturnReleasesAsync(entry), r => Assert.Equal(ReleaseStatus.Cancelled, r.Status));
        Assert.False(await _fixture._db.Context.PurchaseContractsAllocations.AsNoTracking()
            .AnyAsync(x => x.StorageTransactionKey == entry.Key));
        Assert.Equal(40_000m,
            (await _fixture._db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).AllocatedVolume);
    }
}
