using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

public class PurchaseDischargeTransactionsSyncServiceTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    [Fact]
    public async Task Closing_with_loss_creates_a_16_allocated_negative_on_the_cif_contract()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        var shipping = await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 980m, loss: 10m);

        var tx = Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(StorageTransactionType.PurchaseDischargeLoss, tx.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, tx.TransactionStatus);
        Assert.Equal(TransactionCode.PurchaseDischarge, tx.TransactionOrigin);
        Assert.Equal(30m, tx.NetWeight);
        Assert.Equal("F0001", tx.CardCode);
        Assert.Equal(_ctx.Item.Key, tx.DischargeSalesInvoiceItemKey);
        Assert.Equal(shipping.Key, tx.DischargeShippingTransactionKey);
        Assert.Null(tx.ShipmentLoadKey);
        Assert.Null(tx.ShipmentReleaseKey);
        Assert.Null(tx.SalesInvoiceKey);
        Assert.Equal(970m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Warehouse_balance_is_untouched()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        Assert.Equal(0m, await StorageTransactionsWarehouseBalanceService.CalculateAsync(_ctx.Db.Context, "PORTO", "SOJA"));
    }

    [Fact]
    public async Task Closing_with_gain_creates_a_17()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 1012m);

        var tx = Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(StorageTransactionType.PurchaseDischargeGain, tx.TransactionType);
        Assert.Equal(1012m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Fob_contract_gets_nothing()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(FreightTerms.Fob);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        Assert.Empty(await _ctx.DischargesAsync());
        Assert.Equal(1000m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Two_cif_and_one_fob_split_by_load_weight()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var a = await _ctx.SeedContractAsync();
        var b = await _ctx.SeedContractAsync(cardCode: "F0002");
        var fob = await _ctx.SeedContractAsync(FreightTerms.Fob);
        await _ctx.SeedExpeditionAsync(a, 500m);
        await _ctx.SeedExpeditionAsync(b, 300m);
        await _ctx.SeedExpeditionAsync(fob, 200m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 950m);

        Assert.Equal(2, (await _ctx.DischargesAsync()).Count);
        Assert.Equal(475m, await _ctx.AllocatedAsync(a.Key));   // 500 − 25
        Assert.Equal(285m, await _ctx.AllocatedAsync(b.Key));   // 300 − 15
        Assert.Equal(200m, await _ctx.AllocatedAsync(fob.Key)); // FOB: quebra é da Yokotobi
    }

    [Fact]
    public async Task Two_expeditions_on_the_same_contract_sum_right()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 600m);
        await _ctx.SeedExpeditionAsync(contract, 400m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 950m);

        Assert.Equal(950m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Reopening_cancels_and_restores()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Open, delivered: 970m);

        var tx = Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(StorageTransactionsStatus.Cancelled, tx.TransactionStatus);
        Assert.Empty(await _ctx.Db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == tx.Key).ToListAsync());
        Assert.Equal(1000m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Reclosing_with_another_weight_replaces()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 990m);

        var active = (await _ctx.DischargesAsync())
            .Where(x => x.TransactionStatus != StorageTransactionsStatus.Cancelled).ToList();
        Assert.Equal(10m, Assert.Single(active).NetWeight);
        Assert.Equal(990m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Finished_contract_refuses_before_any_write()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(status: ContractStatus.Finished);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Closed, 1000m, 970m, 0m));

        Assert.Contains(contract.Code!, ex.Message);
        Assert.Contains("encerrado", ex.Message);
        Assert.Empty(await _ctx.DischargesAsync());
    }

    [Fact]
    public async Task Reopening_on_a_finished_contract_is_refused_too()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);
        var tracked = await _ctx.Db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.Status = ContractStatus.Finished;
        await _ctx.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Open, 1000m, 970m, 0m));
    }

    [Fact]
    public async Task Gain_above_contract_balance_refuses_before_any_write()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(total: 1000m);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Closed, 1000m, 1012m, 0m));

        Assert.Contains("sobra de descarga", ex.Message);
        Assert.Empty(await _ctx.DischargesAsync());
    }

    [Fact]
    public async Task Return_invoice_items_are_ignored()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        var invoice = await _ctx.Db.Context.SalesInvoices.SingleAsync();
        invoice.InvoiceType = SalesInvoiceType.Return;
        await _ctx.Db.Context.SaveChangesAsync();

        var plan = await _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Closed, 1000m, 970m, 0m);

        Assert.True(plan.IsEmpty);
    }
}
