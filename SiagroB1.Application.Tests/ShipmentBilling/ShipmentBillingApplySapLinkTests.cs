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

    [Fact]
    public void Sap_usage_replaces_the_line_usage()
    {
        var invoice = Invoice();

        ShipmentBillingCreateSalesInvoiceService.ApplySapLink(invoice,
            new SapInvoiceLink(1, 1001, "000000001", "001", null, null, UsageCode: 70, UsageName: "S- Venda p/ Export."));

        var line = invoice.Items.Single();
        Assert.Equal(70, line.UsageCode);
        Assert.Equal("S- Venda p/ Export.", line.UsageName);
    }

    [Fact]
    public void Sap_line_without_usage_keeps_the_resolved_usage()
    {
        var invoice = Invoice();
        var line = invoice.Items.Single();
        line.UsageCode = 1;
        line.UsageName = "Venda de grãos";

        ShipmentBillingCreateSalesInvoiceService.ApplySapLink(invoice, new SapInvoiceLink(1, 1001, "000000001", "001", null, null));

        Assert.Equal(1, line.UsageCode);
        Assert.Equal("Venda de grãos", line.UsageName);
    }
}
