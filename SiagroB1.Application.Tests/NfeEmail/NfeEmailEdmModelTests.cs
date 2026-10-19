using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.NfeEmail;

public class NfeEmailEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("SalesInvoicesSendNfeEmail")]
    [InlineData("PurchaseInvoicesSendNfeEmail")]
    public void Send_action_takes_key_and_recipients(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Collection(Edm.String)", action.Parameters.Single(p => p.Name == "Recipients").Type.FullName());
    }

    [Fact]
    public void Default_recipients_function_exists()
    {
        var f = Model().SchemaElements.OfType<IEdmFunction>().Single(x => x.Name == "NfeEmailDefaultRecipients");

        Assert.Equal("Edm.Int32", f.Parameters.Single(p => p.Name == "DocumentType").Type.FullName());
        Assert.Equal("Edm.Guid", f.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Collection(Edm.String)", f.ReturnType.FullName());
    }

    [Fact]
    public void Messages_entity_set_exists()
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet("NfeEmailMessages"));
    }
}
