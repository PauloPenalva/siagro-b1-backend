using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>O EDM real expõe o que a tela de tributação consome.</summary>
public class TaxationEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void IbsCbsRates_entity_set_uses_edm_date_for_start_date()
    {
        var set = Model().EntityContainer.FindEntitySet("IbsCbsRates");
        Assert.NotNull(set);
        var start = set!.EntityType.FindProperty("StartDate");
        Assert.Equal("Edm.Date", start.Type.FullName());
    }

    [Fact]
    public void Ibs_cbs_totals_are_exposed()
    {
        var model = Model();
        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoicesItems")!.EntityType.FindProperty("TotalIbsCbs"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoices")!.EntityType.FindProperty("TotalInvoiceIbsCbs"));
    }

    [Fact]
    public void TaxCalculationIsActive_is_a_function_with_branch_code()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "TaxCalculationIsActive");
        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "BranchCode").Type.FullName());
        Assert.Equal("Edm.Boolean", function.ReturnType.FullName());
    }
}
