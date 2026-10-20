using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>EDM da transferência entre contas: dinheiro Edm.Double, data string, descrição/observação opcionais.</summary>
public class FinancialAccountTransferEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void The_entity_set_is_exposed_with_its_navigations()
    {
        var type = BuildModel().EntityContainer.FindEntitySet("FinancialAccountTransfers").EntityType;

        Assert.NotNull(type.FindProperty("FromAccount"));
        Assert.NotNull(type.FindProperty("ToAccount"));
        Assert.NotNull(type.FindProperty("Entries"));
    }

    [Theory]
    [InlineData("FromAccountCode", "Edm.String", false)]
    [InlineData("ToAccountCode", "Edm.String", false)]
    [InlineData("TransferDate", "Edm.String", false)]
    [InlineData("Amount", "Edm.Double", false)]
    [InlineData("Description", "Edm.String", true)]
    [InlineData("Notes", "Edm.String", true)]
    public void The_create_action_parameters_have_the_expected_types(string parameter, string type, bool optional)
    {
        var action = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "FinancialAccountTransfersCreate");
        var p = action.Parameters.Single(x => x.Name == parameter);

        Assert.Equal(type, p.Type.FullName());
        Assert.Equal(optional, p is IEdmOptionalParameter);
    }

    [Fact]
    public void The_create_action_returns_a_guid()
    {
        var action = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "FinancialAccountTransfersCreate");

        Assert.Equal("Edm.Guid", action.ReturnType.FullName());
    }
}
