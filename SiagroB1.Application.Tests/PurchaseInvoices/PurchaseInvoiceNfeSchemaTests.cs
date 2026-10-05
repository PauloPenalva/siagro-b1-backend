using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Nfe;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Documento de entrada com a fotografia fiscal, o estado da NF-e e os XMLs (spec §5).</summary>
public class PurchaseInvoiceNfeSchemaTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Both_documents_share_the_nfe_interfaces()
    {
        Assert.IsAssignableFrom<INfeDocument>(new PurchaseInvoice { CardCode = "F1" });
        Assert.IsAssignableFrom<INfeDocument>(new SalesInvoice { CardCode = "C1" });
        Assert.IsAssignableFrom<INfeTaxedLine>(new PurchaseInvoiceItem());
        Assert.IsAssignableFrom<INfeTaxedLine>(new SalesInvoiceItem { ItemCode = "X", UnitOfMeasureCode = "KG" });
    }

    [Fact]
    public void Purchase_invoice_exposes_the_nfe_header_fields()
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(PurchaseInvoice));

        foreach (var name in new[]
                 {
                     "PaymentConditionCode", "IsNfeReturn", "NfeStatus", "NfeEnvironment",
                     "NfeProtocol", "NfeAuthorizedAt", "NfeStatusCode", "NfeStatusReason", "NfeConfirmationError",
                     "TotalInvoiceTaxes", "TotalInvoiceIbsCbs",
                     "VolumeQuantity", "VolumeSpecies", "VolumeBrand", "VolumeNumbering",
                 })
            Assert.NotNull(type.FindProperty(name));
    }

    /// <summary>
    /// A NF-e referenciada (NFref) é 1:N no leiaute; um campo único no cabeçalho modelava errado e saiu.
    /// </summary>
    [Fact]
    public void Purchase_invoice_has_no_single_referenced_access_key()
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(PurchaseInvoice));

        Assert.Null(type.FindProperty("ReferencedAccessKey"));
    }

    [Fact]
    public void Purchase_invoice_item_exposes_the_tax_snapshot()
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(PurchaseInvoiceItem));

        foreach (var name in new[]
                 {
                     "UsageCode", "UsageName", "Cfop", "Ncm", "CstIcms", "IcmsValue", "IbsCbsCst", "CbsValue",
                     "NfeItemNumber", "TotalTaxes", "TotalIbsCbs",
                 })
            Assert.NotNull(type.FindProperty(name));
    }

    [Fact]
    public async Task Latest_authorized_xml_of_a_purchase_invoice_is_found()
    {
        var db = TestDb.CreateUnitOfWork();
        var key = Guid.NewGuid();
        db.Context.PurchaseInvoiceNfeXmls.AddRange(
            new PurchaseInvoiceNfeXml { Key = Guid.NewGuid(), PurchaseInvoiceKey = key, Kind = NfeXmlKind.Signed, Xml = "<signed/>", CreatedAt = DateTime.Now },
            new PurchaseInvoiceNfeXml { Key = Guid.NewGuid(), PurchaseInvoiceKey = key, Kind = NfeXmlKind.Authorized, Xml = "<nfeProc/>", CreatedAt = DateTime.Now });
        await db.SaveChangesAsync();

        Assert.Equal("<nfeProc/>", await db.Context.PurchaseInvoiceNfeXmls.LatestAuthorizedXmlAsync(key));
    }

    [Fact]
    public void Line_totals_follow_the_snapshot()
    {
        var item = new PurchaseInvoiceItem { IcmsValue = 10m, PisValue = 1m, CofinsValue = 2m, CbsValue = 3m, IbsStateValue = 0.5m };
        var invoice = new PurchaseInvoice { CardCode = "F1" };
        invoice.AddItem(item);

        Assert.Equal(13m, item.TotalTaxes);
        Assert.Equal(3.5m, item.TotalIbsCbs);
        Assert.Equal(13m, invoice.TotalInvoiceTaxes);
        Assert.Equal(3.5m, invoice.TotalInvoiceIbsCbs);
    }
}
