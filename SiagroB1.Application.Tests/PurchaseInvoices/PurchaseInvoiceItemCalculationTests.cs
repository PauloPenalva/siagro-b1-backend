using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Propriedades calculadas do documento de entrada e da sua linha.
///
/// A quebra apurada e a diferença migraram de <c>CustomerReturnItem</c> sem mudar de fórmula —
/// estes testes são a prova de que a rotina nova conserva a conciliação da devolução.
/// </summary>
public class PurchaseInvoiceItemCalculationTests
{
    private static SalesInvoiceItem ClosedOrigin(
        decimal quantity = 1000m, decimal delivered = 980m, decimal loss = 0m) =>
        new()
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            DeliveredQuantity = delivered,
            QuantityLoss = loss,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Closed,
        };

    [Fact]
    public void Total_is_quantity_times_unit_price_rounded_to_two_places()
    {
        var line = new PurchaseInvoiceItem { Quantity = 3m, UnitPrice = 1.005m };

        Assert.Equal(3.02m, line.Total);
    }

    [Fact]
    public void Assessed_shortage_comes_from_the_linked_sales_invoice_item()
    {
        var line = new PurchaseInvoiceItem { Quantity = 20m, SalesInvoiceItem = ClosedOrigin() };

        Assert.Equal(20m, line.AssessedShortage);
    }

    [Fact]
    public void Assessed_shortage_is_zero_when_no_origin_is_linked()
    {
        // Linha de entrada NORMAL não tem origem de saída — e isso não é divergência.
        var line = new PurchaseInvoiceItem { Quantity = 20m };

        Assert.Equal(0m, line.AssessedShortage);
    }

    [Fact]
    public void Difference_is_zero_when_the_return_matches_the_shortage()
    {
        var line = new PurchaseInvoiceItem { Quantity = 20m, SalesInvoiceItem = ClosedOrigin() };

        Assert.Equal(0m, line.Difference);
    }

    [Fact]
    public void Difference_is_negative_when_less_was_returned_than_assessed()
    {
        var line = new PurchaseInvoiceItem { Quantity = 15m, SalesInvoiceItem = ClosedOrigin() };

        Assert.Equal(-5m, line.Difference);
    }

    [Fact]
    public void Document_total_sums_the_lines_and_is_independent_of_the_declared_total()
    {
        var invoice = new PurchaseInvoice { CardCode = "F0001", TotalDocumentValue = 1_000m };
        invoice.AddItem(new PurchaseInvoiceItem { Quantity = 2m, UnitPrice = 10m });
        invoice.AddItem(new PurchaseInvoiceItem { Quantity = 3m, UnitPrice = 10m });

        // Divergir do declarado pelo emitente é INFORMAÇÃO de conciliação, não erro: frete e
        // impostos entram no total do documento e não nas linhas.
        Assert.Equal(50m, invoice.TotalInvoiceItems);
        Assert.Equal(1_000m, invoice.TotalDocumentValue);
    }

    [Fact]
    public void Document_is_born_normal_third_party_and_pending()
    {
        var invoice = new PurchaseInvoice { CardCode = "F0001" };

        Assert.Equal(PurchaseInvoiceType.Normal, invoice.InvoiceType);
        Assert.Equal(DocumentIssuerType.ThirdParty, invoice.IssuerType);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
    }
}
