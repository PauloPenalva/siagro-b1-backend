using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// A série escolhida (DocNumberKey) chega no corpo do POST. Se a propriedade sumisse do EDM o OData
/// a descartaria em silêncio e o cadastro cairia sempre na série padrão. A chave também não pode ser obrigatória.
/// </summary>
public class DocNumberKeyEdmTests
{
    [Theory]
    [InlineData("BusinessPartners", "CardCode")]
    [InlineData("Items", "ItemCode")]
    public void DocNumberKey_is_a_nullable_bindable_property(string entitySet, string key)
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var type = builder.GetEdmModel().EntityContainer.FindEntitySet(entitySet)!.EntityType;

        var property = type.FindProperty("DocNumberKey");
        Assert.NotNull(property);
        Assert.True(property!.Type.IsNullable);
        Assert.Contains(type.Key(), k => k.Name == key);
    }
}
