using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>O EDM real expõe o que o "Devolver" consome.</summary>
public class NfeReturnEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Create_nfe_return_is_an_action_with_parallel_double_quantities()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "SalesInvoicesCreateNfeReturn");

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Collection(Edm.Guid)", action.Parameters.Single(p => p.Name == "OriginItemKeys").Type.FullName());
        Assert.Equal("Collection(Edm.Double)", action.Parameters.Single(p => p.Name == "Quantities").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Reason").Type.FullName());
        Assert.Equal("Edm.Guid", action.ReturnType.FullName());
    }

    [Fact]
    public void Returnable_items_is_a_function_with_the_key()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "SalesInvoicesNfeReturnableItems");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.True(function.ReturnType.IsCollection());
    }

    [Fact]
    public void New_columns_are_exposed()
    {
        var model = Model();

        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoices")!.EntityType.FindProperty("IsNfeReturn"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoicesItems")!.EntityType.FindProperty("NfeItemNumber"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("Usages")!.EntityType.FindProperty("ReturnUsageCode"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("Usages")!.EntityType.FindProperty("ReturnUsageName"));
    }
}
