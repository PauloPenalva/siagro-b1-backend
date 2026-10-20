using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.FiscalInventory;

public class FiscalInventoryEdmModelTests
{
    [Fact]
    public void FiscalInventoryOpeningBalances_entity_set_is_exposed()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        var set = builder.GetEdmModel().EntityContainer.FindEntitySet("FiscalInventoryOpeningBalances");

        Assert.NotNull(set);
    }
}
