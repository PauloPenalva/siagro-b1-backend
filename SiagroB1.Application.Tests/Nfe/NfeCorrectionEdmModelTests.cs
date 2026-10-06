using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeCorrectionEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("SalesInvoicesSendNfeCorrection")]
    [InlineData("PurchaseInvoicesSendNfeCorrection")]
    public void Send_correction_takes_key_and_text(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Text").Type.FullName());
        Assert.EndsWith("NfeCorrectionOutcomeDto", action.ReturnType.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeCorrectionXml")]
    [InlineData("PurchaseInvoicesNfeCorrectionXml")]
    public void Correction_xml_is_a_function_with_key_and_sequence(string name)
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == name);

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.Int32", function.Parameters.Single(p => p.Name == "Sequence").Type.FullName());
    }

    [Theory]
    [InlineData("SalesInvoicesNfeCorrections", "SalesInvoiceKey")]
    [InlineData("PurchaseInvoicesNfeCorrections", "PurchaseInvoiceKey")]
    public void Correction_history_is_an_entity_set_without_the_xml(string set, string foreignKey)
    {
        var type = Model().EntityContainer.FindEntitySet(set).EntityType;

        Assert.NotNull(type.FindProperty(foreignKey));
        Assert.NotNull(type.FindProperty("Sequence"));
        Assert.NotNull(type.FindProperty("Text"));
        Assert.Null(type.FindProperty("ProcEventXml"));
    }

    [Theory]
    [InlineData("SalesInvoices")]
    [InlineData("PurchaseInvoices")]
    public void Document_navigates_to_its_corrections(string set)
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet(set).EntityType.FindProperty("NfeCorrections"));
    }
}
