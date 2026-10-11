using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.StorageDeposits;

/// <summary>A tela lê a liberação da remessa e o papel da natureza pelo EDM.</summary>
public class StorageDepositSchemaTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Sales_invoice_exposes_the_release_link()
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(SalesInvoice));

        Assert.NotNull(type.FindProperty("ShipmentReleaseKey"));
        Assert.NotNull(type.FindProperty("ShipmentRelease"));
    }
}
