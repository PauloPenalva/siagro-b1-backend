using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

public class PurchaseInvoiceNfeEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("PurchaseInvoicesIssueNfe")]
    [InlineData("PurchaseInvoicesConsultNfe")]
    [InlineData("PurchaseInvoicesCompleteNfeConfirmation")]
    public void Nfe_actions_take_the_key_and_return_the_outcome(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Fact]
    public void Xml_download_is_a_function_with_the_key()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "PurchaseInvoicesNfeXml");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }

    [Fact]
    public void Create_nfe_return_is_an_action_with_parallel_double_quantities()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "PurchaseInvoicesCreateNfeReturn");

        Assert.Equal("Collection(Edm.Guid)", action.Parameters.Single(p => p.Name == "OriginItemKeys").Type.FullName());
        Assert.Equal("Collection(Edm.Double)", action.Parameters.Single(p => p.Name == "Quantities").Type.FullName());
        Assert.Equal("Collection(Edm.Int32)", action.Parameters.Single(p => p.Name == "ItemNumbers").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Reason").Type.FullName());
        Assert.Equal("Edm.Guid", action.ReturnType.FullName());
    }

    [Fact]
    public void Returnable_items_is_a_collection_function()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "PurchaseInvoicesNfeReturnableItems");

        Assert.True(function.ReturnType.IsCollection());
    }
}
