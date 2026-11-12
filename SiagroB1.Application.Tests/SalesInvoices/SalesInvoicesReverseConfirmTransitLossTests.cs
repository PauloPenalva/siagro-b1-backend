using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
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
}
