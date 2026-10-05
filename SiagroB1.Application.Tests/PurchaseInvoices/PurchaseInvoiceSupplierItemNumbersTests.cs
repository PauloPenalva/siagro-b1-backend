using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// O nItem da NF-e do fornecedor na linha do terceiro: com o XML guardado, todo nItem informado precisa existir na
/// nota (é o que a devolução referencia). A tributação não vem mais do XML (spec terceiro-chave §6.2).
/// </summary>
public class PurchaseInvoiceSupplierItemNumbersTests
{
    private static string Xml() => SupplierNfeXml.Build(
        SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), SupplierNfeXml.IbsCbs()),
        SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m)));

    private static PurchaseInvoice Invoice(DocumentIssuerType issuer, PurchaseInvoiceType type, string? xml, params int?[] numbers)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = issuer, InvoiceType = type,
            XmlData = xml is null ? null : SupplierNfeXml.Bytes(xml),
        };
        foreach (var number in numbers)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
                NfeItemNumber = number, CstIcms = "00",
            });
        return invoice;
    }

    [Fact]
    public void Item_numbers_of_the_xml_pass_and_nothing_is_copied()
    {
        var invoice = Invoice(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, Xml(), 1, 2, null);

        PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        Assert.All(invoice.Items, i => Assert.Equal("00", i.CstIcms));
    }

    [Fact]
    public void Item_number_missing_from_the_xml_is_refused()
    {
        var invoice = Invoice(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, Xml(), 9);

        var e = Assert.Throws<DefaultException>(() => PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items));

        Assert.Equal("Item TRIGO: o item 9 não existe na NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public void Document_without_xml_accepts_any_typed_item_number()
    {
        var invoice = Invoice(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, null, 9);

        PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        Assert.Equal(9, invoice.Items.Single().NfeItemNumber);
    }

    [Theory]
    [InlineData(DocumentIssuerType.Own, PurchaseInvoiceType.Normal)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Return)]
    public void Own_entry_and_customer_return_are_not_checked(DocumentIssuerType issuer, PurchaseInvoiceType type)
    {
        var invoice = Invoice(issuer, type, Xml(), 9);

        PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        Assert.Equal(9, invoice.Items.Single().NfeItemNumber);
    }
}
