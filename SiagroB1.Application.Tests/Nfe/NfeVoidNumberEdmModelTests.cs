using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeVoidNumberEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("SalesInvoicesVoidNfeNumber")]
    [InlineData("PurchaseInvoicesVoidNfeNumber")]
    public void Void_number_takes_key_and_justification(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Justification").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeVoidNumberXml")]
    [InlineData("PurchaseInvoicesNfeVoidNumberXml")]
    public void Void_number_xml_is_a_function_with_the_key(string name)
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == name);

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }
}
