using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Modelo e EDM do complemento de ICMS do documento de saída (spec 2026-10-18): o vínculo mora na linha.</summary>
public class SalesInvoiceIcmsComplementModelTests
{
    [Fact]
    public void Enum_values_are_explicit()
    {
        Assert.Equal(0, (int)SalesInvoiceType.Normal);
        Assert.Equal(1, (int)SalesInvoiceType.Return);
        Assert.Equal(2, (int)SalesInvoiceType.IcmsComplement);
    }

    [Fact]
    public void Complemented_item_fk_is_no_action()
    {
        var db = TestDb.CreateUnitOfWork();
        var foreignKey = db.Context.Model.FindEntityType(typeof(SalesInvoiceItem))!.GetForeignKeys()
            .Single(f => f.Properties.Single().Name == nameof(SalesInvoiceItem.ComplementedItemKey));

        Assert.Equal(typeof(SalesInvoiceItem), foreignKey.PrincipalEntityType.ClrType);
        // Restrict = ON DELETE NO ACTION no SQL Server (mesmo padrão do lado de compra).
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.False(foreignKey.IsRequired);
    }

    [Fact]
    public void Edm_exposes_complemented_item_key()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var type = builder.GetEdmModel().EntityContainer.FindEntitySet("SalesInvoicesItems").EntityType;
        var property = type.FindProperty(nameof(SalesInvoiceItem.ComplementedItemKey));

        Assert.NotNull(property);
        Assert.Equal("Edm.Guid", property.Type.FullName());
        Assert.True(property.Type.IsNullable);
    }
}
