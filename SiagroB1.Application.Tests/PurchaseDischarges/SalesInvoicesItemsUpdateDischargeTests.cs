using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// O gancho pelo caminho do usuário: PATCH da Conferência de Entregas (entidade rastreada e mutada,
/// como o controller entrega).
/// </summary>
public class SalesInvoicesItemsUpdateDischargeTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    private SalesInvoicesItemsUpdateService Service() => new(
        _ctx.Db,
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new ShipmentLoadsClosureHookService(_ctx.Db.Context, new ShipmentLoadsChangeLogService(_ctx.Db.Context)),
        TaxTestServices.InactiveApply(_ctx.Db),
        TaxTestServices.InactiveFiscalComplement(_ctx.Db),
        _ctx.Sync(),
        new TestLogger<SalesInvoicesUpdateService>());

    private async Task<SalesInvoiceItem> PatchAsync(SalesInvoiceDeliveryStatus status, decimal delivered, decimal loss = 0m)
    {
        var tracked = await _ctx.Db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == _ctx.Item.Key);
        tracked.DeliveredQuantity = delivered;
        tracked.QuantityLoss = loss;
        tracked.DeliveryStatus = status;
        await Service().ExecuteAsync(_ctx.Item.Key!.Value, tracked, "tester");
        return tracked;
    }

    [Fact]
    public async Task Closing_creates_the_discharge_and_adjusts_the_purchase_contract()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await PatchAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 980m, loss: 10m);

        Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(970m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Reopening_cancels_the_discharge()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await PatchAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        await PatchAsync(SalesInvoiceDeliveryStatus.Open, delivered: 970m);

        Assert.All(await _ctx.DischargesAsync(),
            x => Assert.Equal(StorageTransactionsStatus.Cancelled, x.TransactionStatus));
        Assert.Equal(1000m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Finished_contract_refuses_and_the_item_is_not_saved()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(status: ContractStatus.Finished);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await Assert.ThrowsAsync<DefaultException>(() =>
            PatchAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m));

        var stored = await _ctx.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == _ctx.Item.Key);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, stored.DeliveryStatus);
        Assert.Empty(await _ctx.DischargesAsync());
    }
}
