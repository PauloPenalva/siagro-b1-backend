using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ContractTemplates;

public class ContractTemplateEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ContractTemplates")]
    [InlineData("CompanySignatories")]
    [InlineData("BusinessPartnerSignatories")]
    public void The_entity_sets_are_exposed(string entitySet)
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));
    }

    [Fact]
    public void List_placeholders_takes_the_scope_as_string()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "ContractTemplatesListPlaceholders");

        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "ContractType").Type.FullName());
    }
}
