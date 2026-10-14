using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Modelo e EDM do complemento de valor próprio (spec 2026-10-14): o vínculo mora na linha.</summary>
public class PurchaseInvoiceValueComplementModelTests
{
    private static IEdmModel EdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Complemented_item_is_a_restrict_self_reference()
    {
        var db = TestDb.CreateUnitOfWork();
        var foreignKey = db.Context.Model.FindEntityType(typeof(PurchaseInvoiceItem))!.GetForeignKeys()
            .Single(f => f.Properties.Single().Name == nameof(PurchaseInvoiceItem.ComplementedItemKey));

        Assert.Equal(typeof(PurchaseInvoiceItem), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.False(foreignKey.IsRequired);
    }

    [Fact]
    public void Item_exposes_the_complemented_item_key_as_a_nullable_guid()
    {
        var type = EdmModel().EntityContainer.FindEntitySet("PurchaseInvoicesItems").EntityType;
        var property = type.FindProperty(nameof(PurchaseInvoiceItem.ComplementedItemKey));

        Assert.NotNull(property);
        Assert.Equal("Edm.Guid", property.Type.FullName());
        Assert.True(property.Type.IsNullable);
    }
}
