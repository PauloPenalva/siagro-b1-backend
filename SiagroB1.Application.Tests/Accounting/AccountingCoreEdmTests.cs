using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Conjuntos e operações do núcleo contábil precisam existir no EDM, senão dão 404.</summary>
public class AccountingCoreEdmTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Model_exposes_periods_and_settings_operations()
    {
        var model = Model();
        Assert.NotNull(model.EntityContainer.FindEntitySet("AccountingPeriods"));
        var names = model.EntityContainer.OperationImports().Select(x => x.Name).ToList();
        Assert.Contains("AccountingPeriodsGenerateYear", names);
        Assert.Contains("AccountingPeriodsSetStatus", names);
        Assert.Contains("AccountingSettingsGet", names);
        Assert.Contains("AccountingSettingsSave", names);
        Assert.Contains("JournalEntriesReverse", names);
        Assert.NotNull(model.EntityContainer.FindEntitySet("JournalEntries")!.EntityType.FindProperty("Lines"));
    }

    [Fact]
    public void Model_exposes_standard_postings_and_catalog()
    {
        var model = Model();
        Assert.NotNull(model.EntityContainer.FindEntitySet("StandardPostings")!.EntityType.FindProperty("Lines"));
        Assert.Contains(model.EntityContainer.OperationImports(), x => x.Name == "StandardPostingCatalogGet");
    }

    [Fact]
    public void Model_exposes_offline_posting_runs_and_request()
    {
        var model = Model();
        Assert.NotNull(model.EntityContainer.FindEntitySet("OfflinePostingRuns")!.EntityType.FindProperty("Items"));
        var request = Assert.Single(model.EntityContainer.OperationImports(), x => x.Name == "OfflinePostingRequest");
        Assert.Equal(["DateFrom", "DateTo", "BranchCode", "Modules", "Simulation"],
            request.Operation.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void Model_exposes_result_closing_operations()
    {
        var names = Model().EntityContainer.OperationImports().Select(x => x.Name).ToList();
        Assert.Contains("AccountingResultClosingRun", names);
        Assert.Contains("AccountingResultClosingReverse", names);
        Assert.Contains("AccountingResultClosingPreview", names);
    }
}
