using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ContractDrafts")]
    [InlineData("ContractDraftSigners")]
    public void The_entity_sets_are_exposed(string entitySet) =>
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));

    [Theory]
    [InlineData("ContractDraftsCreate", "ContractType,ContractKey,TemplateKey,DraftType,Description")]
    [InlineData("ContractDraftsUpdate", "Key,Description,DraftType,BodyHtml")]
    [InlineData("ContractDraftsDelete", "Key")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);
        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Theory]
    [InlineData("ContractDraftsListByContract", "ContractType,ContractKey")]
    [InlineData("ContractDraftsGetBody", "Key")]
    [InlineData("ContractDraftsDownloadPdf", "Key")]
    public void The_functions_declare_their_parameters(string function, string parameters)
    {
        var edmFunction = BuildModel().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == function);
        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmFunction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public void Enums_travel_as_string()
    {
        var create = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "ContractDraftsCreate");
        Assert.Equal("Edm.String", create.Parameters.Single(p => p.Name == "ContractType").Type.FullName());
        Assert.Equal("Edm.String", create.Parameters.Single(p => p.Name == "DraftType").Type.FullName());
    }
}
