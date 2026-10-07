using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractFiscalComplementEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void The_action_codes_travel_as_int32()
    {
        var action = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "SalesContractsSetFiscalComplement");
        foreach (var name in new[] { "UsageCode", "PaymentConditionCode" })
        {
            var parameter = action.Parameters.Single(p => p.Name == name);
            Assert.Equal("Edm.Int32", parameter.Type.Definition.FullTypeName());
        }
    }

    [Fact]
    public void The_function_is_exposed_with_its_key()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "SalesContractsGetFiscalComplement");
        Assert.Contains(function.Parameters, p => p.Name == "Key");
    }
}
