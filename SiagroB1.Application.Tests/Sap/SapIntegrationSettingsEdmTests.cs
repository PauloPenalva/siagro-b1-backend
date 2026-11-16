using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Sap;

/// <summary>
/// O EDM real do serviço para a tela "Integração SAP" (GAC-1226): a tela chama estas operações pelo nome e
/// pelo tipo dos parâmetros.
/// </summary>
public class SapIntegrationSettingsEdmTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Fact]
    public void SapIntegrationSettingsGet_is_a_function_returning_the_settings_model()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "SapIntegrationSettingsGet");

        var type = Assert.IsAssignableFrom<IEdmComplexType>(function.ReturnType.Definition);
        Assert.Equal(
            ["InvoiceSyncEnabled", "InvoiceSyncIntervalMinutes", "InvoiceSyncLastRunAt"],
            type.StructuralProperties().Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public void SapIntegrationSettingsSave_takes_the_toggle_and_the_interval_but_not_the_last_run()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "SapIntegrationSettingsSave");

        Assert.Equal("Edm.Boolean", action.Parameters.Single(p => p.Name == "InvoiceSyncEnabled").Type.FullName());
        Assert.Equal("Edm.Int32", action.Parameters.Single(p => p.Name == "InvoiceSyncIntervalMinutes").Type.FullName());
        Assert.DoesNotContain(action.Parameters, p => p.Name == "InvoiceSyncLastRunAt");
    }

    [Fact]
    public void SapInvoiceSyncRunNow_is_an_unbound_action_without_parameters_returning_the_settings()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "SapInvoiceSyncRunNow");

        Assert.False(action.IsBound);
        Assert.Empty(action.Parameters);
        Assert.Equal("SiagroB1.Domain.Models.SapIntegrationSettingsModel", action.ReturnType.FullName());
    }
}
