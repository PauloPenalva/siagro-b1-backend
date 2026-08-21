using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O EDM real do serviço, montado pelo mesmo <c>ConfigureODataEntities</c> que o Program usa.
/// Propriedade [NotMapped] fora do EDM não existe para a tela: o $select devolve 400 e o
/// binding do UI5 estoura em tempo de execução.
/// </summary>
public class ShipmentLoadEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    private static IEdmEntityType EntityType(string name) => Model()
        .SchemaElements
        .OfType<IEdmEntityType>()
        .Single(t => t.Name == name);

    [Fact]
    public void ShipmentLoads_and_movements_are_exposed_as_entity_sets()
    {
        var container = Model().EntityContainer;

        Assert.NotNull(container.FindEntitySet("ShipmentLoads"));
        Assert.NotNull(container.FindEntitySet("ShipmentLoadMovements"));
    }

    [Fact]
    public void ShipmentLoads_exposes_the_derived_balance()
    {
        // [NotMapped] tira a propriedade do banco E do EDM — o ODataConventionModelBuilder
        // enxerga o atributo. Sem AddProperty explícito, $select=AvailableQuantity dá 400.
        var properties = EntityType(nameof(ShipmentLoad)).Properties().Select(p => p.Name).ToArray();

        Assert.Contains(nameof(ShipmentLoad.AvailableQuantity), properties);
        Assert.Contains(nameof(ShipmentLoad.IsFullyInvoiced), properties);
    }

    [Fact]
    public void ShipmentLoadsCreate_takes_a_collection_of_transaction_keys()
    {
        // CollectionParameter<Guid> não tem precedente neste EDM — este teste é a validação
        // barata do que o plano manda conferir contra o $metadata.
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsCreate");

        var parameter = action.Parameters.Single(p => p.Name == "StorageTransactionKeys");

        Assert.True(parameter.Type.IsCollection());
        Assert.Equal("Edm.Guid", parameter.Type.AsCollection().ElementType().FullName());
    }

    [Fact]
    public void SalesShipmentReleasesGetAvailable_can_include_contracts_without_balance()
    {
        // O parâmetro é o escape do filtro de saldo do contrato. Ele existe justamente para
        // a lista não virar a trava que a decisão de 21/08/2026 removeu do serviço.
        var function = Model()
            .SchemaElements
            .OfType<IEdmFunction>()
            .Single(f => f.Name == "SalesShipmentReleasesGetAvailable");

        var parameter = function.Parameters
            .SingleOrDefault(p => p.Name == "IncludeContractsWithoutBalance");

        Assert.NotNull(parameter);
        Assert.Equal("Edm.Boolean", parameter!.Type.FullName());

        // Opcional (IEdmOptionalParameter), nao anulavel: e o que mantem valida a rota de um
        // parametro so, que continua declarada no controller.
        Assert.IsAssignableFrom<IEdmOptionalParameter>(parameter);
    }
}
