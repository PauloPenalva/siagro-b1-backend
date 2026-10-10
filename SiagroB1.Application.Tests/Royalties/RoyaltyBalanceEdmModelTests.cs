using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Royalties;

/// <summary>Trava que o saldo de royalties e seus totais continuam expostos no metadata do OData.</summary>
public class RoyaltyBalanceEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void RoyaltyTransactionBalances_entity_set_is_keyed_by_the_storage_transaction()
    {
        var set = Model().EntityContainer.EntitySets().Single(s => s.Name == "RoyaltyTransactionBalances");

        Assert.Equal(["StorageTransactionKey"], set.EntityType.Key().Select(k => k.Name));
    }

    [Fact]
    public void RoyaltyBalanceTotals_is_exposed_as_a_function_with_optional_string_parameters()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "RoyaltyBalanceTotals");

        Assert.Equal(8, function.Parameters.Count());
        Assert.All(function.Parameters, p => Assert.Equal("Edm.String", p.Type.FullName()));
        Assert.True(function.ReturnType.IsCollection());
    }
}
