using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Registro de descarga: gravação, guards e as linhas que cada operação deixa no log da carga.
/// </summary>
public class ShipmentLoadDischargesServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadDischargesRecalculateService Recalculate() => new(_db.Context);
    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);

    private ShipmentLoadDischargesCreateService CreateService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesCreateService>.Instance);

    private ShipmentLoadDischargesUpdateService UpdateService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesUpdateService>.Instance);

    private ShipmentLoadDischargesDeleteService DeleteService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesDeleteService>.Instance);

    private ShipmentLoad _load = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;

    private async Task SeedAsync(ShipmentLoadStatus status = ShipmentLoadStatus.Invoiced,
        InvoiceStatus invoiceStatus = InvoiceStatus.Confirmed)
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
            TotalQuantity = 40000m,
        };

        _invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            ShipmentLoadKey = _load.Key,
            InvoiceStatus = invoiceStatus,
        };

        _item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = _invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 40000m,
        };

        _db.Context.ShipmentLoads.Add(_load);
        _db.Context.SalesInvoices.Add(_invoice);
        _db.Context.SalesInvoicesItems.Add(_item);
        await _db.Context.SaveChangesAsync();
    }

    private Task<ShipmentLoadDischarge> CreateAsync(decimal quantity = 39500m, string ticket = "T-1") =>
        CreateService().ExecuteAsync(
            _load.Key, _invoice.Key, _item.Key!.Value,
            ticket, new DateTime(2026, 9, 17), quantity, "descarga na trading", null, "paulo");

    private List<ShipmentLoadChangeLog> LogsOf() =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == _load.Key).ToList();

    [Fact]
    public async Task Create_stores_the_ticket_sums_it_and_logs_it()
    {
        await SeedAsync();

        var discharge = await CreateAsync();

        Assert.Equal("T-1", discharge.TicketNumber);
        Assert.Equal("paulo", discharge.CreatedBy);
        Assert.NotNull(discharge.CreatedAt);
        Assert.Equal(39500m, _item.TicketDeliveredQuantity);
        Assert.Equal(39500m, _load.DischargedQuantity);

        var log = Assert.Single(LogsOf());
        Assert.Equal(ShipmentLoadChangeLogFields.Discharge, log.Field);
        Assert.Null(log.OldValue);
        Assert.Contains("T-1", log.NewValue);
    }

    [Fact]
    public async Task Create_leaves_the_delivery_reconciliation_untouched()
    {
        await SeedAsync();
        _item.DeliveredQuantity = 38000m;
        _item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
        await _db.Context.SaveChangesAsync();

        await CreateAsync();

        Assert.Equal(38000m, _item.DeliveredQuantity);
        Assert.Equal(SalesInvoiceDeliveryStatus.Closed, _item.DeliveryStatus);
        Assert.Equal(39500m, _item.TicketDeliveredQuantity);
    }

    [Fact]
    public async Task Update_changes_the_weight_and_resums()
    {
        await SeedAsync();
        var discharge = await CreateAsync();

        await UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1A", new DateTime(2026, 9, 18), 40100m, "corrigido", "paulo");

        Assert.Equal("T-1A", discharge.TicketNumber);
        Assert.Equal(40100m, discharge.DischargedQuantity);
        Assert.Equal("paulo", discharge.UpdatedBy);
        Assert.Equal(40100m, _item.TicketDeliveredQuantity);
        Assert.Equal(2, LogsOf().Count);
    }

    [Fact]
    public async Task Delete_removes_the_ticket_and_resums()
    {
        await SeedAsync();
        var discharge = await CreateAsync();

        await DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Equal(0m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _load.DischargedQuantity);

        var deletionLog = LogsOf().Last();
        Assert.Contains("T-1", deletionLog.OldValue);
        Assert.Null(deletionLog.NewValue);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled)]
    [InlineData(ShipmentLoadStatus.Returned)]
    public async Task Frozen_load_refuses_the_three_operations(ShipmentLoadStatus status)
    {
        await SeedAsync(ShipmentLoadStatus.Invoiced);
        var discharge = await CreateAsync();

        _load.Status = status;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(1000m, "T-2"));
        await Assert.ThrowsAsync<DefaultException>(() => UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1", new DateTime(2026, 9, 17), 100m, null, "paulo"));
        await Assert.ThrowsAsync<DefaultException>(() =>
            DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo"));
    }

    [Fact]
    public async Task Cancelled_invoice_refuses_a_new_ticket()
    {
        await SeedAsync(invoiceStatus: InvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync());
    }

    [Fact]
    public async Task Item_from_another_invoice_is_refused()
    {
        await SeedAsync();

        var strangerItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = Guid.NewGuid(),
            ItemCode = "MILHO",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
        };
        _db.Context.SalesInvoicesItems.Add(strangerItem);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateService().ExecuteAsync(
            _load.Key, _invoice.Key, strangerItem.Key!.Value,
            "T-9", DateTime.Now.Date, 100m, null, null, "paulo"));
    }

    [Fact]
    public async Task Invoice_from_another_load_is_refused()
    {
        await SeedAsync();

        var otherInvoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C002",
            ShipmentLoadKey = Guid.NewGuid(),
        };
        var otherItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = otherInvoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
        };
        _db.Context.SalesInvoices.Add(otherInvoice);
        _db.Context.SalesInvoicesItems.Add(otherItem);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateService().ExecuteAsync(
            _load.Key, otherInvoice.Key, otherItem.Key!.Value,
            "T-9", DateTime.Now.Date, 100m, null, null, "paulo"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Non_positive_weight_is_refused(decimal quantity)
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(quantity, "T-3"));
    }

    [Fact]
    public async Task Blank_ticket_number_is_refused()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(100m, "   "));
    }
}
