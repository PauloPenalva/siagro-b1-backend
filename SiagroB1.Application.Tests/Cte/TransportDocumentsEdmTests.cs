using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>O conjunto TransportDocuments (com filhos navegáveis) e as ações do CT-e precisam existir no EDM.</summary>
public class TransportDocumentsEdmTests
{
    [Fact]
    public void Model_exposes_transport_documents_with_children_and_actions()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        var type = model.EntityContainer.FindEntitySet("TransportDocuments")!.EntityType;
        foreach (var nav in new[] { "Parties", "NfeKeys", "Quantities", "Components" })
            Assert.NotNull(type.FindProperty(nav));

        var imports = model.EntityContainer.OperationImports().Select(x => x.Name).ToList();
        Assert.Contains("TransportDocumentsSave", imports);
        Assert.Contains("TransportDocumentsImportNfe", imports);
        Assert.Contains("TransportDocumentsDelete", imports);
        Assert.Contains("TransportDocumentsConfirm", imports);
        Assert.Contains("TransportDocumentsReopen", imports);
        Assert.Contains("TransportDocumentsDiscardNumber", imports);
    }
}
