using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.FreightOrders;

public class FreightOrdersEdmModelTests
{
    private static IEdmModel Build()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Model_exposes_freight_orders()
    {
        var type = Build().EntityContainer.FindEntitySet("FreightOrders")!.EntityType;
        Assert.Contains(type.DeclaredKey!, k => k.Name == "Key");
    }

    [Theory]
    [InlineData("FreightOrdersSave", new[] { "Model" })]
    [InlineData("FreightOrdersClose", new[] { "Key" })]
    [InlineData("FreightOrdersCancel", new[] { "Key" })]
    [InlineData("FreightOrdersCreateCte", new[] { "Key" })]
    public void Model_exposes_freight_order_actions(string name, string[] parameters)
    {
        var model = Build();
        var action = model.SchemaElements.OfType<IEdmAction>().SingleOrDefault(a => a.Name == name);
        Assert.NotNull(action);
        Assert.NotNull(model.EntityContainer.FindOperationImports(name).FirstOrDefault());
        foreach (var p in parameters) Assert.Contains(action.Parameters, x => x.Name == p);
    }
}
