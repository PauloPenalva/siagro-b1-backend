using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialEdmModelTests
{
    private static Microsoft.OData.Edm.IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("FinancialAccounts")]
    [InlineData("FinancialDocuments")]
    [InlineData("FinancialSettlements")]
    public void The_entity_sets_are_exposed(string entitySet)
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));
    }

    [Theory]
    [InlineData(nameof(FinancialDocument.OpenAmount))]
    [InlineData(nameof(FinancialDocument.IsBlockedForSettlement))]
    [InlineData(nameof(FinancialDocument.IsOverdue))]
    [InlineData(nameof(FinancialDocument.AvailableAdvanceAmount))]
    public void The_computed_properties_survive_into_the_edm(string property)
    {
        var type = BuildModel().EntityContainer
            .FindEntitySet("FinancialDocuments").EntityType;

        Assert.NotNull(type.FindProperty(property));
    }

    [Theory]
    [InlineData("FinancialDocumentsSettle", "Amount")]
    [InlineData("FinancialDocumentsSettle", "InterestAmount")]
    public void Money_parameters_are_double_never_decimal(string action, string parameter)
    {
        var operation = BuildModel().SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == action);

        var type = operation.Parameters.Single(p => p.Name == parameter).Type;

        Assert.Equal("Edm.Double", type.FullName());
    }

    [Fact]
    public void The_advance_refund_action_declares_its_parameters()
    {
        var model = BuildModel();

        var action = model.SchemaElements.OfType<IEdmAction>()
            .Single(x => x.Name == "FinancialAdvancesRefund");

        Assert.Contains(action.Parameters, p => p.Name == "DocumentKey");
        Assert.Contains(action.Parameters, p => p.Name == "FinancialAccountCode");
        Assert.Contains(action.Parameters, p => p.Name == "RefundDate");
        Assert.Contains(action.Parameters, p => p.Name == "DocumentReference");
        Assert.Contains(action.Parameters, p => p.Name == "Reason");
    }

    [Fact]
    public void The_advance_relink_action_declares_its_parameters()
    {
        var model = BuildModel();

        var action = model.SchemaElements.OfType<IEdmAction>()
            .Single(x => x.Name == "FinancialAdvancesRelinkContract");

        Assert.Contains(action.Parameters, p => p.Name == "DocumentKey");
        Assert.Contains(action.Parameters, p => p.Name == "ContractType");
        Assert.Contains(action.Parameters, p => p.Name == "ContractKey");
    }
}
