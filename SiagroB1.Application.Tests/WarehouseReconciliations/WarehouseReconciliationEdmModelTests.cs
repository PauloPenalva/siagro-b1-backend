using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("WarehouseReconciliations")]
    [InlineData("WarehouseReconciliationReasons")]
    [InlineData("WarehouseReconciliationAttachments")]
    public void The_entity_sets_are_exposed(string entitySet)
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));
    }

    [Theory]
    [InlineData("WarehouseReconciliationsSendApproval", "Key")]
    [InlineData("WarehouseReconciliationsWithdrawApproval", "Key")]
    [InlineData("WarehouseReconciliationsApproval", "Key,Comments")]
    [InlineData("WarehouseReconciliationsReject", "Key,Comments")]
    [InlineData("WarehouseReconciliationsCancel", "Key,Reason")]
    [InlineData("WarehouseReconciliationsDistributeLoss", "Key,ShipmentReleaseKeys,Quantities")]
    [InlineData("WarehouseReconciliationsAttachmentUpload", "ReconciliationKey,Description,File,FileName,ContentType")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Theory]
    [InlineData("WarehouseReconciliationsGetBalancePreview", "WarehouseCode,ItemCode,ReferenceDate")]
    [InlineData("WarehouseReconciliationsAttachmentsList", "ReconciliationKey")]
    [InlineData("WarehouseReconciliationsAttachmentsDownload", "Key")]
    [InlineData("WarehouseReconciliationsListReleases", "Key")]
    public void The_functions_declare_their_parameters(string function, string parameters)
    {
        var edmFunction = BuildModel().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == function);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmFunction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public void Reference_date_is_a_string_parameter()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "WarehouseReconciliationsGetBalancePreview");

        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "ReferenceDate").Type.FullName());
    }
}
