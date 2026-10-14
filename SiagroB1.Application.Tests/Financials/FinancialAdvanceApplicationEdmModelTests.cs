using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>EDM da compensação de adiantamento (spec 2026-10-14): chaves Guid, dinheiro Edm.Double, data string.</summary>
public class FinancialAdvanceApplicationEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("Key", "Edm.Guid")]
    [InlineData("AdvanceKey", "Edm.Guid")]
    [InlineData("Amount", "Edm.Double")]
    [InlineData("SettlementDate", "Edm.String")]
    public void The_apply_action_parameters_have_the_expected_types(string parameter, string type)
    {
        var action = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "FinancialDocumentsApplyAdvance");

        Assert.Equal(type, action.Parameters.Single(p => p.Name == parameter).Type.FullName());
    }

    [Fact]
    public void The_candidates_function_takes_the_key_and_returns_a_collection()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "FinancialDocumentsGetAdvanceCandidates");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.True(function.ReturnType.IsCollection());
    }

    [Theory]
    [InlineData(nameof(FinancialDocument.AppliedAmount))]
    [InlineData(nameof(FinancialDocument.AvailableAdvanceAmount))]
    public void Applied_and_credit_are_in_the_edm(string property)
    {
        var type = BuildModel().EntityContainer.FindEntitySet("FinancialDocuments").EntityType;

        Assert.NotNull(type.FindProperty(property));
    }
}
