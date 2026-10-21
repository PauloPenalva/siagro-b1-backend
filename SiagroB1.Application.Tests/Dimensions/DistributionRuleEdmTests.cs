using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Dimensions;

/// <summary>O conjunto DistributionRules e as actions de versão precisam existir no EDM, senão dão 404.</summary>
public class DistributionRuleEdmTests
{
    [Fact]
    public void Model_exposes_rules_with_versions_and_version_actions()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        var type = model.EntityContainer.FindEntitySet("DistributionRules")!.EntityType;
        Assert.NotNull(type.FindProperty("Versions"));

        var save = model.EntityContainer.OperationImports().Single(x => x.Name == "DistributionRulesSaveVersion");
        Assert.Equal(
            ["Code", "ValidFrom", "LineCostCenterCodes", "LinePercentages"],
            save.Operation.Parameters.Select(x => x.Name));
        Assert.Contains(model.EntityContainer.OperationImports(), x => x.Name == "DistributionRulesDeleteVersion");
    }
}
