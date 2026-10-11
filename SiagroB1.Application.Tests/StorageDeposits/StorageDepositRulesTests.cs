using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class StorageDepositRulesTests
{
    [Fact]
    public async Task Active_remittance_ignores_cancelled_ones()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Cancelled);
        var active = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var found = await StorageDepositRules.FindActiveRemittanceAsync(db.Context, seed.Release.Key);

        Assert.Equal(active.Key, found!.Key);
        Assert.Single(found.Items);
    }

    [Fact]
    public async Task Returned_counts_pending_and_confirmed_but_not_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        await seed.AddReturnAsync(remittance, 30_000m, InvoiceStatus.Pending);
        await seed.AddReturnAsync(remittance, 20_000m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 99_000m, InvoiceStatus.Cancelled);
        var itemKey = remittance.Items.Single().Key!.Value;

        var returned = await StorageDepositRules.ReturnedByRemittanceItemAsync(db.Context, [itemKey], null);

        Assert.Equal(50_000m, returned[itemKey]);
    }

    [Fact]
    public async Task Returned_can_exclude_one_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var mine = await seed.AddReturnAsync(remittance, 30_000m);
        await seed.AddReturnAsync(remittance, 20_000m);
        var itemKey = remittance.Items.Single().Key!.Value;

        var returned = await StorageDepositRules.ReturnedByRemittanceItemAsync(db.Context, [itemKey], mine.Key);

        Assert.Equal(20_000m, returned[itemKey]);
    }

    [Fact]
    public async Task Shipped_transactions_of_a_standard_release_are_the_purchases()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var purchase = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(30_000m, StorageTransactionType.SalesShipment);
        await seed.AddShipmentAsync(1_000m, StorageTransactionType.PurchaseReturn);

        var keys = await StorageDepositRules.ShippedTransactions(db.Context, seed.Release.Key, ReleaseOrigin.Standard)
            .Select(t => t.Key).ToListAsync();

        Assert.Equal([purchase.Key], keys);
    }

    [Fact]
    public async Task Warehouse_reconciliation_purchase_is_not_a_shipment()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddShipmentAsync(500m, StorageTransactionType.Purchase, TransactionCode.WarehouseReconciliation);

        Assert.False(await StorageDepositRules
            .ShippedTransactions(db.Context, seed.Release.Key, ReleaseOrigin.Standard).AnyAsync());
    }

    [Fact]
    public async Task Release_without_purchase_leg_ships_by_sales_shipment()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var sales = await seed.AddShipmentAsync(30_000m, StorageTransactionType.SalesShipment);
        await seed.AddShipmentAsync(500m, StorageTransactionType.WarehouseLoss);

        var keys = await StorageDepositRules
            .ShippedTransactions(db.Context, seed.Release.Key, ReleaseOrigin.OwnershipTransfer)
            .Select(t => t.Key).ToListAsync();

        Assert.Equal([sales.Key], keys);
    }

    [Fact]
    public async Task Release_with_active_remittance_refuses_the_action()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            StorageDepositRules.EnsureNoActiveRemittanceAsync(db.Context, seed.Release.Key, "cancelar"));

        Assert.Equal("A liberação tem a remessa para depósito nº 000000123 ativa. Cancele a remessa antes de cancelar a liberação.", e.Message);
    }

    [Fact]
    public async Task Reconciled_transaction_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 30_000m);
        var shipment = await seed.AddShipmentAsync(30_000m);
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
            { PurchaseInvoiceKey = ret.Key, StorageTransactionKey = shipment.Key });
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            StorageDepositRules.EnsureTransactionsNotReconciledAsync(db.Context, [shipment.Key]));

        Assert.Equal($"O romaneio {shipment.Code} está reconciliado com o retorno de depósito nº 000000456. Desfaça a reconciliação antes.", e.Message);
    }

    /// <summary>InMemory esconde LINQ intraduzível: as consultas precisam virar SQL no provedor real.</summary>
    [Fact]
    public void Queries_translate_to_sql_server()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);

        Assert.NotEmpty(StorageDepositRules.ActiveRemittances(ctx).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.ShippedTransactions(ctx, Guid.NewGuid(), ReleaseOrigin.Standard).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.ShippedTransactions(ctx, Guid.NewGuid(), ReleaseOrigin.OwnershipTransfer).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.ReturnLinesQuery(ctx, [Guid.NewGuid()], Guid.NewGuid()).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.ReturnsOfRemittanceItem(ctx, Guid.NewGuid()).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.TransactionsWithReturn(ctx, Guid.NewGuid(), ReleaseOrigin.Standard).ToQueryString());
    }
}
