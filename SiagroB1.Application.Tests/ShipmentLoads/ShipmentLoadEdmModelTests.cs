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

    [Theory]
    [InlineData("ShipmentLoadsAttachTransactions")]
    [InlineData("ShipmentLoadsDetachTransactions")]
    public void The_shipment_link_actions_take_a_collection_of_transaction_keys(string actionName)
    {
        // CollectionParameter<Guid> não tem precedente neste EDM — este teste é a validação
        // barata do que o plano manda conferir contra o $metadata. Migrou de
        // ShipmentLoadsCreate junto com o parâmetro: a criação deixou de conhecer romaneio
        // quando a carga passou a nascer do planejamento da Logística.
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == actionName);

        var parameter = action.Parameters.Single(p => p.Name == "StorageTransactionKeys");

        Assert.True(parameter.Type.IsCollection());
        Assert.Equal("Edm.Guid", parameter.Type.AsCollection().ElementType().FullName());
    }

    /// <summary>
    /// O formulário da Logística tem muitos campos opcionais, e um parâmetro que falte no EDM
    /// derruba a action inteira com um 500 de corpo vazio.
    /// </summary>
    [Theory]
    [InlineData("ShipmentLoadsCreate")]
    [InlineData("ShipmentLoadsUpdate")]
    public void The_logistics_form_actions_expose_every_field(string actionName)
    {
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == actionName);

        var parameters = action.Parameters.Select(p => p.Name).ToList();

        Assert.Contains("BranchCode", parameters);
        Assert.Contains("TruckCode", parameters);
        Assert.Contains("TruckDriverCode", parameters);
        Assert.Contains("CarrierCardCode", parameters);
        Assert.Contains("CarrierName", parameters);
        Assert.Contains("ItemCode", parameters);
        Assert.Contains("UnitOfMeasureCode", parameters);
        Assert.Contains("WarehouseCode", parameters);
        Assert.Contains("CardCode", parameters);
        Assert.Contains("HasExcess", parameters);
        Assert.Contains("FreightPrice", parameters);
        Assert.Contains("Comments", parameters);

        // Edm.Double, NUNCA Edm.Decimal: decimal faz parse para string no cliente e o backend
        // devolve 400 que não nomeia o campo.
        var freight = action.Parameters.Single(p => p.Name == "FreightPrice");
        Assert.Equal("Edm.Double", freight.Type.Definition.FullTypeName());
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

    /// <summary>
    /// O terceiro termo do saldo precisa existir no EDM: as duas telas mostram a quantidade
    /// devolvida ao armazém, e sem a propriedade o $select devolve 400.
    /// </summary>
    [Fact]
    public void ShipmentLoads_exposes_the_returned_to_warehouse_quantity()
    {
        var properties = EntityType(nameof(ShipmentLoad)).Properties().Select(p => p.Name).ToArray();

        Assert.Contains(nameof(ShipmentLoad.ReturnedToWarehouseQuantity), properties);
    }

    /// <summary>
    /// A ação de recusa: arrays PARALELOS de chaves e quantidades.
    /// </summary>
    [Fact]
    public void ShipmentLoadsRefuse_takes_parallel_collections_of_keys_and_quantities()
    {
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsRefuse");

        var parameters = action.Parameters.Select(p => p.Name).ToArray();

        Assert.Contains("Key", parameters);
        Assert.Contains("SalesInvoiceKeys", parameters);
        Assert.Contains("Quantities", parameters);
        Assert.Contains("Destination", parameters);
        Assert.Contains("DestinationWarehouseCode", parameters);
        Assert.Contains("Reason", parameters);

        Assert.Equal(
            "Collection(Edm.Guid)",
            action.Parameters.Single(p => p.Name == "SalesInvoiceKeys").Type.Definition.FullTypeName());

        // ⚠️ Collection(Edm.Double), NUNCA Edm.Decimal: decimal faz o cliente serializar o
        // número como string e o backend devolve 400 que não nomeia o campo.
        Assert.Equal(
            "Collection(Edm.Double)",
            action.Parameters.Single(p => p.Name == "Quantities").Type.Definition.FullTypeName());

        // Destination é string ("Rebilling" | "Warehouse"), não enum: não há precedente de enum
        // em parâmetro de action neste EDM.
        Assert.Equal(
            "Edm.String",
            action.Parameters.Single(p => p.Name == "Destination").Type.Definition.FullTypeName());

        // Só o armazém é opcional — ele não se aplica ao destino de refaturamento.
        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "DestinationWarehouseCode"));
    }

    [Fact]
    public void ShipmentLoadsGetRefusableDocuments_is_a_function_returning_a_collection()
    {
        var function = Model()
            .SchemaElements
            .OfType<IEdmFunction>()
            .Single(f => f.Name == "ShipmentLoadsGetRefusableDocuments");

        Assert.Equal(
            "Edm.Guid",
            function.Parameters.Single(p => p.Name == "Key").Type.Definition.FullTypeName());

        Assert.True(function.ReturnType.IsCollection());
    }
}
