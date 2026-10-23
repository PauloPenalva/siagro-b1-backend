using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>O conjunto TransportDocuments (com filhos navegáveis) e as ações/funções do CT-e precisam existir no EDM.</summary>
public class CteEdmModelTests
{
    private static IEdmModel Build()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Model_exposes_transport_documents_with_children()
    {
        var model = Build();
        var type = model.EntityContainer.FindEntitySet("TransportDocuments")!.EntityType;
        foreach (var nav in new[] { "Parties", "NfeKeys", "Quantities", "Components" })
            Assert.NotNull(type.FindProperty(nav));
    }

    [Fact]
    public void Model_exposes_cte_numbers_to_void_entity_set()
    {
        Assert.NotNull(Build().EntityContainer.FindEntitySet("CteNumbersToVoid"));
    }

    [Theory]
    [InlineData("BranchCteSettingsSave")]
    [InlineData("TransportDocumentsSave")]
    [InlineData("TransportDocumentsImportNfe")]
    [InlineData("TransportDocumentsDelete")]
    [InlineData("TransportDocumentsConfirm")]
    [InlineData("TransportDocumentsReopen")]
    [InlineData("TransportDocumentsDiscardNumber")]
    [InlineData("TransportDocumentsTransmit")]
    [InlineData("TransportDocumentsConsult")]
    [InlineData("TransportDocumentsTransmitBatch")]
    [InlineData("TransportDocumentsEndorseBatch")]
    [InlineData("TransportDocumentsXmlBatch")]
    [InlineData("TransportDocumentsCancel")]
    [InlineData("TransportDocumentsSendCorrection")]
    [InlineData("CteNumbersVoid")]
    public void Model_exposes_cte_actions(string name)
    {
        var action = Build().SchemaElements.OfType<IEdmAction>().SingleOrDefault(a => a.Name == name);
        Assert.NotNull(action);
        Assert.NotNull(Build().EntityContainer.FindOperationImports(name).FirstOrDefault());
    }

    [Theory]
    [InlineData("BranchCteSettingsGet", "BranchCode")]
    [InlineData("TransportDocumentsXml", "Key")]
    public void Model_exposes_cte_functions(string name, string parameter)
    {
        var function = Build().SchemaElements.OfType<IEdmFunction>().SingleOrDefault(f => f.Name == name);
        Assert.NotNull(function);
        Assert.Contains(function.Parameters, p => p.Name == parameter);
    }
}
