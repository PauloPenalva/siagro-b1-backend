using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeCancellationEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("SalesInvoicesCancelNfe")]
    [InlineData("PurchaseInvoicesCancelNfe")]
    public void Cancel_nfe_takes_key_and_justification(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Justification").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesCompleteNfeCancellation")]
    [InlineData("PurchaseInvoicesCompleteNfeCancellation")]
    public void Complete_cancellation_takes_the_key(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeCancellationXml")]
    [InlineData("PurchaseInvoicesNfeCancellationXml")]
    public void Cancellation_xml_is_a_function_with_the_key(string name)
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == name);

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }

    [Theory]
    [InlineData("SalesInvoice")]
    [InlineData("PurchaseInvoice")]
    public void Cancellation_fields_are_in_the_entity(string entity)
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == entity);

        foreach (var property in new[] { "NfeCancellationProtocol", "NfeCancelledAt", "NfeCancellationReason", "NfeCancellationError" })
            Assert.NotNull(type.FindProperty(property));
    }
}
