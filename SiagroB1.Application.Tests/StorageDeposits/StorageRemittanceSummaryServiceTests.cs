using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class StorageRemittanceSummaryServiceTests
{
    private static ShipmentReleasesStorageRemittanceSummaryService Service(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, TestConfiguration.Erp(erp));

    [Fact]
    public async Task Without_remittance_everything_is_zero()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddShipmentAsync(10_000m);

        var summary = await Service(db).ExecuteAsync(seed.Release.Key);

        Assert.Null(summary.RemittanceKey);
        Assert.Equal(0m, summary.RemittedQuantity);
        Assert.Equal(10_000m, summary.ShippedWithoutReturnQuantity);
    }

    [Fact]
    public async Task Balances_follow_returns_and_reconciliations()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 29_500m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 50_000m, InvoiceStatus.Cancelled);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(20_000m);
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation { PurchaseInvoiceKey = ret.Key, StorageTransactionKey = s1.Key });
        await db.SaveChangesAsync();

        var summary = await Service(db).ExecuteAsync(seed.Release.Key);

        Assert.Equal(remittance.Key, summary.RemittanceKey);
        Assert.Equal(100_000m, summary.RemittedQuantity);
        Assert.Equal(29_500m, summary.ReturnedQuantity);
        Assert.Equal(70_500m, summary.PendingReturnQuantity);
        Assert.Equal(20_000m, summary.ShippedWithoutReturnQuantity);
        Assert.Single(summary.Returns);
        Assert.Equal(2, summary.Transactions.Count);
        Assert.Equal(ret.Key, summary.Transactions.Single(t => t.Key == s1.Key).ReturnPurchaseInvoiceKey);
    }

    [Fact]
    public async Task Remittance_with_two_lines_does_not_blow_up_and_sums_them()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(60_000m);
        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = remittance.Key, ItemCode = StorageDepositSeed.Item, UnitOfMeasureCode = "KG",
            Quantity = 40_000m, UsageCode = seed.RemittanceUsageCode,
        });
        await db.SaveChangesAsync();

        var summary = await Service(db).ExecuteAsync(seed.Release.Key);

        Assert.Equal(100_000m, summary.RemittedQuantity);
        Assert.Equal(100_000m, summary.PendingReturnQuantity);
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var db = TestDb.CreateUnitOfWork();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db, "SAPB1").ExecuteAsync(Guid.NewGuid()));
        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }
}
