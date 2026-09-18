using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Escritor único das quantidades derivadas do ticket. O teste que mais importa aqui é o que
/// prova o que o serviço NÃO faz: encostar na conferência de entrega.
/// </summary>
public class ShipmentLoadDischargesRecalculateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadDischargesRecalculateService Service() => new(_db.Context);

    private async Task<(ShipmentLoad Load, SalesInvoice Invoice, SalesInvoiceItem Item)> SeedAsync(
        SalesInvoiceDeliveryStatus deliveryStatus = SalesInvoiceDeliveryStatus.Open,
        decimal deliveredQuantity = 0m)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = ShipmentLoadStatus.Invoiced,
            TotalQuantity = 40000m,
        };

        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C001", ShipmentLoadKey = load.Key };

        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 40000m,
            DeliveredQuantity = deliveredQuantity,
            DeliveryStatus = deliveryStatus,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.SalesInvoicesItems.Add(item);
        await _db.Context.SaveChangesAsync();

        return (load, invoice, item);
    }

    private ShipmentLoadDischarge AddTicket(ShipmentLoad load, SalesInvoice invoice, SalesInvoiceItem item, decimal weight)
    {
        var discharge = new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            SalesInvoiceKey = invoice.Key,
            SalesInvoiceItemKey = item.Key,
            TicketNumber = "T1",
            DischargeDate = DateTime.Now.Date,
            DischargedQuantity = weight,
        };
        _db.Context.ShipmentLoadsDischarges.Add(discharge);
        return discharge;
    }

    [Fact]
    public async Task Sums_every_ticket_of_the_item_and_of_the_load()
    {
        var (load, invoice, item) = await SeedAsync();
        AddTicket(load, invoice, item, 25000m);
        AddTicket(load, invoice, item, 14500m);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(39500m, item.TicketDeliveredQuantity);
        Assert.Equal(39500m, load.DischargedQuantity);
    }

    [Theory]
    [InlineData(SalesInvoiceDeliveryStatus.Open)]
    [InlineData(SalesInvoiceDeliveryStatus.Closed)]
    public async Task Never_touches_the_delivery_reconciliation(SalesInvoiceDeliveryStatus status)
    {
        // A regra central do GAC-1171: o ticket alimenta o campo dele e só. Vale nos DOIS
        // estados da entrega — a conferência é mandatória e soberana.
        var (load, invoice, item) = await SeedAsync(status, deliveredQuantity: 38000m);
        item.QuantityLoss = 120m;
        await _db.Context.SaveChangesAsync();

        AddTicket(load, invoice, item, 39500m);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(39500m, item.TicketDeliveredQuantity);
        Assert.Equal(38000m, item.DeliveredQuantity);
        Assert.Equal(120m, item.QuantityLoss);
        Assert.Equal(status, item.DeliveryStatus);
    }

    [Fact]
    public async Task Zeroes_the_item_when_the_last_ticket_is_gone()
    {
        var (load, invoice, item) = await SeedAsync(deliveredQuantity: 38000m);
        AddTicket(load, invoice, item, 39500m);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        _db.Context.ShipmentLoadsDischarges.RemoveRange(_db.Context.ShipmentLoadsDischarges);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, item.TicketDeliveredQuantity);
        Assert.Equal(0m, load.DischargedQuantity);
        // O conferido continua de pé: excluir ticket não apaga a conferência.
        Assert.Equal(38000m, item.DeliveredQuantity);
    }

    [Fact]
    public async Task Discounts_a_removed_ticket_before_the_delete_is_saved()
    {
        // Mesmo padrão que a inclusão já promove (ticket e recálculo no mesmo SaveChanges),
        // só que do lado da exclusão: o Remove ainda não foi salvo quando o recálculo roda.
        var (load, invoice, item) = await SeedAsync();
        AddTicket(load, invoice, item, 25000m);
        var toRemove = AddTicket(load, invoice, item, 14500m);
        await _db.Context.SaveChangesAsync();

        _db.Context.ShipmentLoadsDischarges.Remove(toRemove);

        await Service().RecalculateAsync(load.Key, [item.Key!.Value]);

        Assert.Equal(25000m, item.TicketDeliveredQuantity);
        Assert.Equal(25000m, load.DischargedQuantity);
    }

    [Fact]
    public async Task Ignores_unknown_item_keys_without_throwing()
    {
        var (load, _, _) = await SeedAsync();

        await Service().RecalculateAsync(load.Key, [Guid.NewGuid()]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, load.DischargedQuantity);
    }
}
