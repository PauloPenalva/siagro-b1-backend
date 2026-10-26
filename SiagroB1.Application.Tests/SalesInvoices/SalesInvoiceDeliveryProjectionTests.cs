using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// GAC-1195: derivadas de descarga e conferência no cabeçalho do documento de saída, para o grid
/// "Documentos de Saída" do detalhe da carga. Puras: dependem só de <c>Items</c> estar carregado.
/// </summary>
public class SalesInvoiceDeliveryProjectionTests
{
    private static SalesInvoiceItem Item(
        decimal quantity,
        decimal returned = 0m,
        decimal ticket = 0m,
        decimal delivered = 0m,
        SalesInvoiceDeliveryStatus status = SalesInvoiceDeliveryStatus.Open) => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        Quantity = quantity,
        ReturnedQuantity = returned,
        TicketDeliveredQuantity = ticket,
        DeliveredQuantity = delivered,
        DeliveryStatus = status,
    };

    private static SalesInvoice Invoice(decimal netWeight, decimal returned, params SalesInvoiceItem[] items)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            NetWeight = netWeight,
            ReturnedQuantity = returned,
        };
        foreach (var item in items) invoice.AddItem(item);
        return invoice;
    }

    [Fact]
    public void Effective_quantity_is_billed_minus_returned()
    {
        var invoice = Invoice(40_000m, 12_500m, Item(40_000m, returned: 12_500m));

        Assert.Equal(27_500m, invoice.EffectiveQuantity);
    }

    [Fact]
    public void Ticket_and_delivered_quantities_sum_the_lines()
    {
        var invoice = Invoice(30_000m, 0m,
            Item(10_000m, ticket: 9_950m, delivered: 9_900m),
            Item(20_000m, ticket: 19_800m, delivered: 19_850m));

        Assert.Equal(29_750m, invoice.TicketDeliveredQuantity);
        Assert.Equal(29_750m, invoice.DeliveredQuantity);
    }

    [Fact]
    public void Discharged_requires_a_ticket_on_every_line_that_still_has_quantity()
    {
        var partial = Invoice(30_000m, 0m, Item(10_000m, ticket: 9_950m), Item(20_000m));
        var full = Invoice(30_000m, 0m, Item(10_000m, ticket: 9_950m), Item(20_000m, ticket: 1m));

        Assert.False(partial.IsDischarged);
        Assert.True(full.IsDischarged);
    }

    /// <summary>A linha que voltou inteira não chegou ao destino e não espera ticket (mesma regra da carga).</summary>
    [Fact]
    public void A_fully_returned_line_does_not_wait_for_a_ticket()
    {
        var invoice = Invoice(30_000m, 10_000m,
            Item(10_000m, returned: 10_000m),
            Item(20_000m, ticket: 19_900m));

        Assert.True(invoice.IsDischarged);
    }

    [Fact]
    public void A_fully_returned_invoice_is_not_discharged()
    {
        var invoice = Invoice(10_000m, 10_000m, Item(10_000m, returned: 10_000m));

        Assert.False(invoice.IsDischarged);
    }

    [Fact]
    public void Delivery_closed_only_when_every_line_is_closed()
    {
        var open = Invoice(30_000m, 0m,
            Item(10_000m, status: SalesInvoiceDeliveryStatus.Closed), Item(20_000m));
        var closed = Invoice(30_000m, 0m,
            Item(10_000m, status: SalesInvoiceDeliveryStatus.Closed),
            Item(20_000m, status: SalesInvoiceDeliveryStatus.Closed));

        Assert.False(open.IsDeliveryClosed);
        Assert.True(closed.IsDeliveryClosed);
    }

    [Fact]
    public void Without_lines_nothing_is_discharged_or_closed()
    {
        var invoice = Invoice(0m, 0m);

        Assert.False(invoice.IsDischarged);
        Assert.False(invoice.IsDeliveryClosed);
        Assert.Equal(0m, invoice.TicketDeliveredQuantity);
    }
}
