using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

public class PurchaseDischargesListServiceTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    [Fact]
    public async Task Lists_active_and_cancelled_with_contract_load_and_invoice()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 990m); // 1 cancelada + 1 ativa

        var today = DateTime.Now.Date;
        var rows = await new PurchaseDischargesListService(_ctx.Db).ExecuteAsync(today, today);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(contract.Code, r.PurchaseContractCode);
            Assert.Equal("CG000001", r.ShipmentLoadCode);
            Assert.Equal(StorageTransactionType.PurchaseDischargeLoss, r.TransactionType);
        });
        Assert.Single(rows, r => r.Status == StorageTransactionsStatus.Cancelled);
    }

    [Fact]
    public async Task Date_range_is_inclusive_of_the_last_day_and_excludes_other_days()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        var today = DateTime.Now.Date;
        Assert.Single(await new PurchaseDischargesListService(_ctx.Db).ExecuteAsync(today.AddDays(-5), today));
        Assert.Empty(await new PurchaseDischargesListService(_ctx.Db).ExecuteAsync(today.AddDays(-5), today.AddDays(-1)));
    }
}
