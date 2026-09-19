using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// FKs NoAction + InMemory sem FK: é a trava, e não o banco, que estes testes verificam (mesma
/// lição do GAC-1171). Romaneio 16/17 CANCELADO não trava nada.
/// </summary>
public class PurchaseDischargeGuardsTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    private async Task<Guid> SeedClosedAsync()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        var shipping = await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);
        return shipping.Key;
    }

    [Fact]
    public async Task Active_discharge_is_detected_by_item_invoice_and_shipping()
    {
        var shippingKey = await SeedClosedAsync();

        Assert.True(await PurchaseDischargeGuards.HasActiveForItemAsync(_ctx.Db.Context, _ctx.Item.Key!.Value));
        Assert.True(await PurchaseDischargeGuards.HasActiveForInvoiceAsync(_ctx.Db.Context, _ctx.Invoice.Key));
        Assert.True(await PurchaseDischargeGuards.HasActiveForShippingAsync(_ctx.Db.Context, shippingKey));
    }

    [Fact]
    public async Task Cancelled_discharge_does_not_block()
    {
        var shippingKey = await SeedClosedAsync();
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Open, delivered: 970m);

        Assert.False(await PurchaseDischargeGuards.HasActiveForItemAsync(_ctx.Db.Context, _ctx.Item.Key!.Value));
        Assert.False(await PurchaseDischargeGuards.HasActiveForInvoiceAsync(_ctx.Db.Context, _ctx.Invoice.Key));
        Assert.False(await PurchaseDischargeGuards.HasActiveForShippingAsync(_ctx.Db.Context, shippingKey));
    }

    [Fact]
    public async Task Invoice_delete_is_refused()
    {
        await SeedClosedAsync();
        var invoice = await _ctx.Db.Context.SalesInvoices.SingleAsync(x => x.Key == _ctx.Invoice.Key);
        invoice.InvoiceStatus = InvoiceStatus.Pending; // delete só aceita Pending
        await _ctx.Db.Context.SaveChangesAsync();

        var service = new SalesInvoicesDeleteService(
            _ctx.Db,
            new ShipmentLoadsBalanceHookService(_ctx.Db.Context, new ShipmentLoadsMovementLogService(_ctx.Db.Context)),
            NullLogger<SalesInvoicesDeleteService>.Instance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(_ctx.Invoice.Key, "tester"));
        Assert.Equal(PurchaseDischargeGuards.InvoiceMessage, ex.Message);
        Assert.True(await _ctx.Db.Context.SalesInvoices.AnyAsync(x => x.Key == _ctx.Invoice.Key));
    }

    [Fact]
    public async Task Item_delete_is_refused()
    {
        await SeedClosedAsync();

        var service = new SalesInvoicesItemsDeleteService(_ctx.Db, NullLogger<SalesInvoicesItemsDeleteService>.Instance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(_ctx.Item.Key!.Value));
        Assert.Equal(PurchaseDischargeGuards.ItemMessage, ex.Message);
        Assert.True(await _ctx.Db.Context.SalesInvoicesItems.AnyAsync(x => x.Key == _ctx.Item.Key));
    }

    [Fact]
    public async Task Invoice_cancel_is_refused()
    {
        await SeedClosedAsync();

        var service = new SalesInvoicesCancelService(
            _ctx.Db,
            new SalesShipmentReleasesRecalculateShippedService(_ctx.Db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(_ctx.Db),
            new ShipmentLoadsBalanceHookService(_ctx.Db.Context, new ShipmentLoadsMovementLogService(_ctx.Db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(_ctx.Invoice.Key, "tester"));
        Assert.Equal(PurchaseDischargeGuards.InvoiceMessage, ex.Message);
        var stored = await _ctx.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == _ctx.Invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, stored.InvoiceStatus);
    }

    [Fact]
    public async Task Expedition_reverse_is_refused()
    {
        await SeedClosedAsync();
        var shipping = await _ctx.Db.Context.ShippingTransactions.AsNoTracking().SingleAsync();
        var salesBefore = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == shipping.SalesStorageTransactionKey);
        var purchaseBefore = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == shipping.PurchaseStorageTransactionKey);

        var service = new ShippingTransactionsReverseService(
            _ctx.Db,
            new StorageTransactionsGetService(_ctx.Db, NullLogger<StorageTransactionsGetService>.Instance),
            new PurchaseContractsAllocationDeleteService(_ctx.Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            new ShipmentReleasesRecalculateShippedService(_ctx.Db.Context),
            NullLogger<ShippingTransactionsReverseService>.Instance);

        // O estorno recebe a chave da SAÍDA(7), não a de SHIPPING_TRANSACTIONS. A mensagem de
        // negócio precisa virar 4xx (ApplicationException) — o controller não mapeia
        // DefaultException para um resultado de erro.
        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            service.ExecuteAsync(shipping.SalesStorageTransactionKey, "tester"));
        Assert.Equal(PurchaseDischargeGuards.ShippingMessage, ex.Message);

        // Nada mudou: a Expedição continua viva e os status da perna 8/7 intactos.
        Assert.True(await _ctx.Db.Context.ShippingTransactions.AnyAsync(x => x.Key == shipping.Key));
        var salesAfter = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == shipping.SalesStorageTransactionKey);
        var purchaseAfter = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == shipping.PurchaseStorageTransactionKey);
        Assert.Equal(salesBefore.TransactionStatus, salesAfter.TransactionStatus);
        Assert.Equal(purchaseBefore.TransactionStatus, purchaseAfter.TransactionStatus);
    }

    [Fact]
    public async Task ChangeRelease_BuildPlan_IsRefused_WhenShippingHasActiveDischarge()
    {
        await SeedClosedAsync();
        var shipping = await _ctx.Db.Context.ShippingTransactions.AsNoTracking().SingleAsync();

        var service = new ShippingTransactionsChangeReleaseService(
            _ctx.Db,
            _ctx.StorageCreate(),
            new StorageTransactionsConfirmedService(
                _ctx.Db,
                new FakeStringLocalizer<Resource>(),
                new ShipmentReleasesRecalculateShippedService(_ctx.Db.Context),
                new ShipmentReleaseMovementGuardService(_ctx.Db.Context),
                NullLogger<StorageTransactionsConfirmedService>.Instance),
            new PurchaseContractsAllocationCreateService(
                _ctx.Db, new StorageTransactionsGetService(_ctx.Db, NullLogger<StorageTransactionsGetService>.Instance)),
            new ShipmentReleasesRecalculateShippedService(_ctx.Db.Context),
            new ShipmentLoadsMovementLogService(_ctx.Db.Context),
            new ShippingReleaseChangeReconciliationGuard(_ctx.Db.Context),
            new FakeStorageAddressBalanceReader(100_000m));

        // GAC-1177 permite trocar a liberação de uma Expedição já numa carga faturada — mas com
        // um 16/17 ativo o acerto do fornecedor ficaria órfão no contrato antigo.
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(
            [new ShippingReleaseChangeItem(shipping.SalesStorageTransactionKey, Guid.NewGuid())],
            "motivo", "tester"));

        Assert.Equal(PurchaseDischargeGuards.ShippingMessage, ex.Message);
        Assert.Empty(await _ctx.Db.Context.ShippingReleaseChanges.ToListAsync());
    }

    [Fact]
    public async Task ManualAllocationDelete_OfActiveDischarge_IsRefused()
    {
        await SeedClosedAsync();
        var discharge = Assert.Single(await _ctx.DischargesAsync());
        var alloc = await _ctx.Db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == discharge.Key);

        var service = new PurchaseContractsAllocationDeleteService(
            _ctx.Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance);

        // Estorno manual (tela do contrato): soltar a alocação do 16/17 por aqui deixaria o
        // romaneio ativo, sem alocação — quem desfaz é reabrir a Conferência de Entregas.
        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            service.ExecuteWithTransactionAsync(alloc.Key, "tester"));

        Assert.Equal(
            "Esta alocação é de quebra/sobra de descarga da troca nota. Reabra a Conferência de Entregas para desfazê-la.",
            ex.Message);
        Assert.True(await _ctx.Db.Context.PurchaseContractsAllocations.AnyAsync(x => x.Key == alloc.Key));
    }
}
