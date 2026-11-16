using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.ShipmentBilling;

/// <summary>GAC-1226: o CFOP da NF do SAP só substitui o resolvido quando o SAP o informa.</summary>
public class ShipmentBillingApplySapLinkTests
{
    private static SalesInvoice Invoice() => new()
    {
        CardCode = "C1",
        Items = [new SalesInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Cfop = "5102" }],
    };

    [Fact]
    public void Null_sap_cfop_keeps_the_resolved_cfop()
    {
        var invoice = Invoice();

        ShipmentBillingCreateSalesInvoiceService.ApplySapLink(invoice, new SapInvoiceLink(1, 1001, "000000001", "001", null, null));

        Assert.Equal("5102", invoice.Items.Single().Cfop);
    }

    [Fact]
    public void Sap_cfop_replaces_the_resolved_cfop()
    {
        var invoice = Invoice();

        ShipmentBillingCreateSalesInvoiceService.ApplySapLink(invoice, new SapInvoiceLink(1, 1001, "000000001", "001", null, "5101"));

        Assert.Equal("5101", invoice.Items.Single().Cfop);
    }
}
