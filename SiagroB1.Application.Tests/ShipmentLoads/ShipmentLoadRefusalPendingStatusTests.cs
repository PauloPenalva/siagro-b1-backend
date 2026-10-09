using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadRefusalPendingStatusTests
{
    [Theory]
    [InlineData(40_000, 40_000, 0, 0, false)]  // seria Invoiced
    [InlineData(40_000, 10_000, 0, 0, false)]  // seria PartiallyInvoiced
    [InlineData(40_000, 0, 0, 40_000, true)]   // seria InTransshipment
    public void Pending_refusal_wins_every_other_status(
        decimal total, decimal invoiced, decimal returned, decimal transshipped, bool openTransshipment)
    {
        Assert.Equal(
            ShipmentLoadStatus.RefusalPending,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                total, invoiced, returned, transshipped, openTransshipment, hasPendingRefusal: true));
    }

    [Fact]
    public void Without_pending_refusal_nothing_changes()
    {
        Assert.Equal(
            ShipmentLoadStatus.Invoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(40_000m, 40_000m, 0m, 0m, false));
    }

    private static async Task<(SiagroB1.Infra.UnitOfWork Db, ShipmentLoad Load, StorageTransaction Shipment)> SeedInvoicedLoadAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            TotalQuantity = 30_000m, Status = ShipmentLoadStatus.Invoiced,
        };
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = "C1", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", BranchCode = "01", GrossWeight = 30_000m, NetWeight = 30_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Invoiced, ShipmentLoadKey = load.Key,
        };
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", ShipmentLoadKey = load.Key,
            InvoiceType = SalesInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed,
        };
        invoice.AddItem(new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 30_000m });

        db.Context.ShipmentLoads.Add(load);
        db.Context.StorageTransactions.Add(shipment);
        db.Context.SalesInvoices.Add(invoice);
        db.Context.ShipmentLoadRefusals.Add(new ShipmentLoadRefusal
        {
            ShipmentLoadKey = load.Key, Destination = RefusalDestination.Rebilling, Reason = "Recusa",
        });
        await db.SaveChangesAsync();

        return (db, load, shipment);
    }

    [Fact]
    public async Task Recalculate_writes_refusal_pending_and_keeps_the_shipments_out_of_assembly()
    {
        var (db, load, shipment) = await SeedInvoicedLoadAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
        await db.SaveChangesAsync();

        Assert.Equal(ShipmentLoadStatus.RefusalPending,
            (await db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).Status);
        // Review Focus 1: Confirmed devolveria o romaneio à Montagem com a mercadoria faturada.
        Assert.Equal(StorageTransactionsStatus.Invoiced,
            (await db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key)).TransactionStatus);
    }

    [Fact]
    public async Task Completed_or_cancelled_refusal_does_not_lock_the_load()
    {
        var (db, load, _) = await SeedInvoicedLoadAsync();
        (await db.Context.ShipmentLoadRefusals.SingleAsync()).Status = ShipmentLoadRefusalStatus.Cancelled;
        await db.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
        await db.SaveChangesAsync();

        Assert.Equal(ShipmentLoadStatus.Invoiced,
            (await db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).Status);
    }
}
