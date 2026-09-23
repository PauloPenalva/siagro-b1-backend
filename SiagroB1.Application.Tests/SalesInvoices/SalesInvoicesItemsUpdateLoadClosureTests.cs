using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// GAC-1171 (melhorias): encerrar ou estornar a entrega na Conferência
/// (/sales-invoices/reconciliation e /open-reconciliation) recalcula a situação da carga.
/// </summary>
/// <remarks>
/// Mesmo formato do PATCH real: o controller carrega o item RASTREADO e muta a mesma instância,
/// ver <see cref="SalesInvoicesItemsUpdateServiceTests"/>.
/// </remarks>
public class SalesInvoicesItemsUpdateLoadClosureTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesItemsUpdateService Service() => new(
        _db,
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new ShipmentLoadsChangeLogService(_db.Context),
        new TestLogger<SalesInvoicesUpdateService>());

    /// <summary>Carga Faturada de 100 t com uma nota confirmada de dois itens de 50 t.</summary>
    private async Task<(ShipmentLoad Load, SalesInvoiceItem First, SalesInvoiceItem Second)> SeedAsync(
        SalesInvoiceDeliveryStatus first,
        SalesInvoiceDeliveryStatus second,
        ShipmentLoadStatus loadStatus = ShipmentLoadStatus.Invoiced,
        bool isDischarged = false,
        bool withLoad = true)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000051",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 100_000,
            InvoicedQuantity = 100_000,
            Status = loadStatus,
            IsDischarged = isDischarged,
        };
        _db.Context.ShipmentLoads.Add(load);

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000888",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = withLoad ? load.Key : null,
        };

        SalesInvoiceItem Item(SalesInvoiceDeliveryStatus delivery) => new()
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 50_000,
            DeliveredQuantity = delivery == SalesInvoiceDeliveryStatus.Closed ? 50_000 : 0m,
            DeliveryStatus = delivery,
        };

        var a = Item(first);
        var b = Item(second);
        invoice.Items.Add(a);
        invoice.Items.Add(b);
        _db.Context.SalesInvoices.Add(invoice);

        await _db.Context.SaveChangesAsync();
        return (load, a, b);
    }

    private async Task CloseAsync(SalesInvoiceItem item)
    {
        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveredQuantity = 50_000;
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
        await Service().ExecuteAsync(item.Key!.Value, tracked, "conferente");
    }

    private async Task ReopenAsync(SalesInvoiceItem item)
    {
        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;
        await Service().ExecuteAsync(item.Key!.Value, tracked, "conferente");
    }

    private Task<ShipmentLoad> LoadAsync() => _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    [Fact]
    public async Task Closing_the_last_open_delivery_completes_the_load_and_logs_who_did_it()
    {
        var (_, _, second) = await SeedAsync(SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Open);

        await CloseAsync(second);

        Assert.Equal(ShipmentLoadStatus.Completed, (await LoadAsync()).Status);

        var log = await _db.Context.ShipmentLoadsChangeLogs.SingleAsync();
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal("Faturada", log.OldValue);
        Assert.Equal("Concluída", log.NewValue);
        Assert.Equal("conferente", log.ChangedBy);
    }

    [Fact]
    public async Task Closing_one_of_two_deliveries_keeps_the_load_invoiced_without_log()
    {
        var (_, first, _) = await SeedAsync(SalesInvoiceDeliveryStatus.Open, SalesInvoiceDeliveryStatus.Open);

        await CloseAsync(first);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync()).Status);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }

    [Fact]
    public async Task Reopening_a_delivery_of_a_completed_load_returns_it_to_invoiced()
    {
        var (_, first, _) = await SeedAsync(
            SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Closed, ShipmentLoadStatus.Completed);

        await ReopenAsync(first);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync()).Status);
        Assert.Equal("Concluída", (await _db.Context.ShipmentLoadsChangeLogs.SingleAsync()).OldValue);
    }

    [Fact]
    public async Task Reopening_a_delivery_of_a_completed_and_marked_load_returns_it_to_discharged()
    {
        var (_, first, _) = await SeedAsync(
            SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Closed,
            ShipmentLoadStatus.Completed, isDischarged: true);

        await ReopenAsync(first);

        Assert.Equal(ShipmentLoadStatus.Discharged, (await LoadAsync()).Status);
    }

    [Fact]
    public async Task An_invoice_outside_any_load_leaves_loads_alone()
    {
        var (_, _, second) = await SeedAsync(
            SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Open, withLoad: false);

        await CloseAsync(second);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync()).Status);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }
}
