using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Transport;

public class TransportEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Truck_combinations_entity_set_exists_with_trailers()
    {
        var set = Model().EntityContainer.FindEntitySet("TruckCombinations");

        Assert.NotNull(set);
        Assert.NotNull(set.EntityType.FindProperty("Trailers"));
    }

    [Fact]
    public void Save_action_takes_trailer_codes_collection()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "TruckCombinationsSave");

        Assert.Equal("Collection(Edm.String)", action.Parameters.Single(p => p.Name == "TrailerCodes").Type.FullName());
        Assert.Equal("Edm.Boolean", action.Parameters.Single(p => p.Name == "IsNew").Type.FullName());
    }

    [Fact]
    public void Truck_kind_is_exposed_as_enum()
    {
        var truck = Model().EntityContainer.FindEntitySet("Trucks").EntityType;

        Assert.Equal("SiagroB1.Domain.Enums.TruckKind", truck.FindProperty("Kind").Type.FullName());
    }
}
