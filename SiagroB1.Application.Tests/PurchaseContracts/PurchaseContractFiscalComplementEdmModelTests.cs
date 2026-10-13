using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void The_action_usage_code_travels_as_nullable_int32()
    {
        var action = BuildModel().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "PurchaseContractsSetFiscalComplement");
        var parameter = action.Parameters.Single(p => p.Name == "UsageCode");
        Assert.Equal("Edm.Int32", parameter.Type.Definition.FullTypeName());
        Assert.True(parameter.Type.IsNullable);
    }

    [Fact]
    public void The_function_is_exposed_with_its_key()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "PurchaseContractsGetFiscalComplement");
        Assert.Contains(function.Parameters, p => p.Name == "Key");
    }
}
