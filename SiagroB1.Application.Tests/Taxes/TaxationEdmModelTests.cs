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
}
