using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>O conjunto TransportManifests (com filhos navegáveis), as ações/funções do MDF-e e o vínculo no CT-e precisam existir no EDM.</summary>
public class MdfeEdmModelTests
{
    private static IEdmModel Build()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Model_exposes_transport_manifests_with_children()
    {
        var type = Build().EntityContainer.FindEntitySet("TransportManifests")!.EntityType;
        Assert.Contains(type.DeclaredKey!, k => k.Name == "Key");
        foreach (var nav in new[] { "Ctes", "LoadingCities", "RouteUfs", "Vehicles", "Drivers", "Contractors", "Payments", "Seals", "Xmls", "Events" })
            Assert.NotNull(type.FindProperty(nav));
        foreach (var prop in new[] { "Status", "Number", "AccessKey", "ClosedOn", "ClosedAt", "RejectionReason", "CteCount", "GrossWeight" })
            Assert.NotNull(type.FindProperty(prop));
    }

    [Fact]
    public void Manifest_status_and_dates_have_expected_edm_types()
    {
        var type = Build().EntityContainer.FindEntitySet("TransportManifests")!.EntityType;
        Assert.Equal(EdmTypeKind.Enum, type.FindProperty("Status")!.Type.TypeKind());
        Assert.Equal(EdmPrimitiveTypeKind.Date, type.FindProperty("ClosedOn")!.Type.AsPrimitive().PrimitiveKind());
        Assert.Equal(EdmPrimitiveTypeKind.DateTimeOffset, type.FindProperty("ClosedAt")!.Type.AsPrimitive().PrimitiveKind());
    }

    [Fact]
    public void Cte_link_status_is_the_same_enum_as_manifest_status()
    {
        var model = Build();
        var manifestStatus = model.EntityContainer.FindEntitySet("TransportManifests")!.EntityType.FindProperty("Status")!.Type.Definition;
        var function = model.SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "TransportDocumentsManifest");
        var linkStatus = ((IEdmStructuredType)function.ReturnType.Definition).FindProperty("Status")!.Type.Definition;
        Assert.Equal(EdmTypeKind.Enum, linkStatus.TypeKind);
        Assert.Same(manifestStatus, linkStatus);
    }

    [Fact]
    public void Manifest_cte_link_exposes_cte_navigation()
    {
        var link = Build().EntityContainer.FindEntitySet("TransportManifests")!.EntityType
            .DeclaredNavigationProperties().Single(n => n.Name == "Ctes").ToEntityType();
        Assert.NotNull(link.FindProperty("TransportDocument"));
        Assert.NotNull(link.FindProperty("CteAccessKey"));
    }

    [Theory]
    [InlineData("BranchMdfeSettingsSave", new[] { "BranchCode", "Enabled", "Environment", "DefaultSeries", "DefaultCargoType", "RequiresCiot", "Series", "Descriptions", "NextNumbers" })]
    [InlineData("TransportManifestsCreateFromCtes", new[] { "BranchCode", "CteKeys" })]
    [InlineData("TransportManifestsSave", new[] { "Key", "Model" })]
    [InlineData("TransportManifestsDelete", new[] { "Key" })]
    [InlineData("TransportManifestsConfirm", new[] { "Key" })]
    [InlineData("TransportManifestsReopen", new[] { "Key" })]
    [InlineData("TransportManifestsDiscardNumber", new[] { "Key" })]
    [InlineData("TransportManifestsTransmit", new[] { "Key" })]
    [InlineData("TransportManifestsConsult", new[] { "Key" })]
    [InlineData("TransportManifestsClose", new[] { "Key", "ClosedOn", "UfCode", "CityIbge" })]
    public void Model_exposes_mdfe_actions(string name, string[] parameters)
    {
        var model = Build();
        var action = model.SchemaElements.OfType<IEdmAction>().SingleOrDefault(a => a.Name == name);
        Assert.NotNull(action);
        Assert.NotNull(model.EntityContainer.FindOperationImports(name).FirstOrDefault());
        foreach (var p in parameters) Assert.Contains(action.Parameters, x => x.Name == p);
    }

    [Fact]
    public void Close_takes_closed_on_as_string_not_edm_date()
    {
        var action = Build().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "TransportManifestsClose");
        var closedOn = action.Parameters.Single(p => p.Name == "ClosedOn");
        Assert.Equal(EdmPrimitiveTypeKind.String, closedOn.Type.AsPrimitive().PrimitiveKind());
        Assert.True(closedOn.Type.IsNullable);
    }

    [Theory]
    [InlineData("BranchMdfeSettingsGet", "BranchCode")]
    [InlineData("MdfeEligibleCtes", "BranchCode")]
    [InlineData("MdfeNotClosed", "BranchCode")]
    [InlineData("TransportManifestsXml", "Key")]
    [InlineData("TransportDocumentsManifest", "Key")]
    public void Model_exposes_mdfe_functions(string name, string parameter)
    {
        var function = Build().SchemaElements.OfType<IEdmFunction>().SingleOrDefault(f => f.Name == name);
        Assert.NotNull(function);
        Assert.Contains(function.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public void Cte_manifest_link_function_returns_complex_type_with_expected_properties()
    {
        var function = Build().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "TransportDocumentsManifest");
        var type = Assert.IsAssignableFrom<IEdmStructuredType>(function.ReturnType.Definition);
        foreach (var prop in new[] { "ManifestKey", "Number", "Series", "Status" })
            Assert.NotNull(type.FindProperty(prop));
    }

    [Fact]
    public void Mdfe_not_closed_function_returns_collection_with_flags_and_shared_status_enum()
    {
        var model = Build();
        var function = model.SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "MdfeNotClosed");
        var type = Assert.IsAssignableFrom<IEdmStructuredType>(((IEdmCollectionType)function.ReturnType.Definition).ElementType.Definition);
        foreach (var prop in new[] { "AccessKey", "Protocol", "ManifestKey", "Number", "Series", "Status", "TractionPlate", "StatusDiverges", "OutsideSystem" })
            Assert.NotNull(type.FindProperty(prop));
        var manifestStatus = model.EntityContainer.FindEntitySet("TransportManifests")!.EntityType.FindProperty("Status")!.Type.Definition;
        Assert.Same(manifestStatus, type.FindProperty("Status")!.Type.Definition);
    }
}
