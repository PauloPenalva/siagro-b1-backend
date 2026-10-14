using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>EDM dos títulos firmes (spec 2026-10-13): chaves Guid, dinheiro Edm.Double, function com Key.</summary>
public class FinancialFirmPayablesEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    private static string ParameterType(string action, string parameter) =>
        BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action)
            .Parameters.Single(p => p.Name == parameter).Type.FullName();

    [Theory]
    [InlineData("FinancialDocumentsCompose", "Key", "Edm.Guid")]
    [InlineData("FinancialDocumentsCompose", "ComposedKeys", "Collection(Edm.Guid)")]
    [InlineData("FinancialDocumentsUncompose", "CompositionKey", "Edm.Guid")]
    [InlineData("FinancialDocumentsDation", "Key", "Edm.Guid")]
    [InlineData("FinancialDocumentsDation", "Amount", "Edm.Double")]
    [InlineData("FinancialDocumentsDation", "SettlementDate", "Edm.String")]
    [InlineData("FinancialDocumentsDation", "Notes", "Edm.String")]
    [InlineData("FinancialDocumentsNetting", "Key", "Edm.Guid")]
    [InlineData("FinancialDocumentsNetting", "CounterpartKey", "Edm.Guid")]
    public void Action_parameters_have_the_expected_types(string action, string parameter, string type)
    {
        Assert.Equal(type, ParameterType(action, parameter));
    }

    [Fact]
    public void The_composition_function_takes_the_key()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "FinancialDocumentsGetComposition");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }
}
