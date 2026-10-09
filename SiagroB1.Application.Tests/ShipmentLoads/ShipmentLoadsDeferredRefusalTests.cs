using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>Recusa de carga na filial que emite NF-e pelo Siagro: o registro (spec 2026-10-09 §5.1).</summary>
public class ShipmentLoadsDeferredRefusalTests
{
    private static RefusalRequest Request(NfeLoadRefusalScenario s, decimal quantity,
        RefusalDestination destination = RefusalDestination.Rebilling, string? warehouse = null) =>
        new(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, quantity)).ToList(), destination, warehouse,
            "Recusado por umidade");

    [Fact]
    public async Task Registers_a_pending_refusal_with_one_pending_own_return_per_document()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents: 2);

        var result = await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            Request(s, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse), "tester");

        var refusal = await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync();
        Assert.Equal(refusal.Key, result.RefusalKey);
        Assert.Equal(ShipmentLoadRefusalStatus.Pending, refusal.Status);
        Assert.Equal(RefusalDestination.Warehouse, refusal.Destination);
        Assert.Equal("ARM99", refusal.DestinationWarehouseCode);
        Assert.Equal("ARMAZEM RETAGUARDA", refusal.DestinationWarehouseName);
        Assert.Equal("Recusado por umidade", refusal.Reason);

        var returns = await s.Db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items)
            .Where(i => i.InvoiceType == SalesInvoiceType.Return).ToListAsync();
        Assert.Equal(2, returns.Count);
        Assert.All(returns, r =>
        {
            Assert.True(r.IsNfeReturn);
            Assert.Equal(InvoiceStatus.Pending, r.InvoiceStatus);
            Assert.Equal(refusal.Key, r.ShipmentLoadRefusalKey);
            Assert.Equal(s.Load.Key, r.ShipmentLoadKey);
            Assert.Equal(10_000m, r.Items.Single().Quantity);
            Assert.StartsWith("Recusa da carga CG000001. Devolução da NF-e", r.Comments);
            Assert.EndsWith("Motivo: Recusado por umidade", r.Comments);
        });
    }

    [Fact]
    public async Task The_load_is_locked_and_its_balance_untouched()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();

        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            Request(s, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse), "tester");

        var load = await s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.RefusalPending, load.Status);
        Assert.Equal(30_000m, load.InvoicedQuantity);
        Assert.Equal(0m, load.ReturnedToWarehouseQuantity);
        Assert.False(await s.Db.Context.StorageTransactions.AnyAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn));
        Assert.False(await s.Db.Context.ShipmentLoadsTransshipments.AnyAsync());
        Assert.Contains(await s.Db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync(),
            m => m.MovementType == ShipmentLoadMovementType.Refused && m.Description!.Contains("aguardando NF-e de entrada"));
    }

    [Fact]
    public async Task Document_confirmed_but_not_transmitted_is_refused()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.SaleKeys[0])).NfeStatus = NfeStatus.None;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(Request(s, 1m), "tester"));

        Assert.Equal("O documento 000002388 não tem NF-e autorizada: transmita a NF-e ou cancele o documento.", ex.Message);
        Assert.False(await s.Db.Context.ShipmentLoadRefusals.AnyAsync());
    }

    [Fact]
    public async Task Document_with_cancelled_nfe_keeps_the_lock_message()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.SaleKeys[0])).NfeStatus = NfeStatus.Cancelled;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(Request(s, 1m), "tester"));

        Assert.Equal("A NF-e deste documento foi cancelada na SEFAZ: o documento não pode mudar.", ex.Message);
    }

    [Fact]
    public async Task Sale_usage_without_return_usage_is_refused_without_writing()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();
        (await s.Db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos")).ReturnUsageCode = null;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(Request(s, 1m), "tester"));

        Assert.Contains("não tem natureza de devolução cadastrada", ex.Message);
        Assert.False(await s.Db.Context.ShipmentLoadRefusals.AnyAsync());
        Assert.False(await s.Db.Context.SalesInvoices.AnyAsync(i => i.InvoiceType == SalesInvoiceType.Return));
    }

    [Fact]
    public async Task Outside_the_rule_the_refusal_stays_synchronous()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();

        var result = await NfeLoadRefusalTestSeed.RefuseService(s.Db, erp: "SAPB1").ExecuteAsync(Request(s, 10_000m), "tester");

        Assert.Null(result.RefusalKey);
        Assert.False(await s.Db.Context.ShipmentLoadRefusals.AnyAsync());
        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, (await s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync()).Status);
    }
}
