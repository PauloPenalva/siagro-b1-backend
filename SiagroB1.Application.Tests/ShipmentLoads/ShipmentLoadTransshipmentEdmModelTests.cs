using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O EDM é contrato com a tela: action ou propriedade que não está aqui devolve 404/400 no
/// navegador, e nenhum teste de serviço acusa. Molde de <see cref="ShipmentLoadDischargeEdmModelTests"/>.
/// </summary>
public class ShipmentLoadTransshipmentEdmModelTests
{
    // Mesmo helper de ShipmentLoadDischargeEdmModelTests: o EDM real, montado pelo
    // ConfigureODataEntities que o Program usa.
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Fact]
    public void Declares_the_transshipments_entity_set()
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet("ShipmentLoadsTransshipments"));
    }

    [Theory]
    [InlineData("ShipmentLoadsTransshipmentStart")]
    [InlineData("ShipmentLoadsTransshipmentRegisterEntry")]
    [InlineData("ShipmentLoadsTransshipmentReverse")]
    public void Declares_the_write_actions(string name)
    {
        Assert.Contains(Model().SchemaElements.OfType<IEdmAction>(), a => a.Name == name);
    }

    [Fact]
    public void Exposes_the_derived_load_balance_terms()
    {
        var load = (IEdmStructuredType)Model().FindDeclaredType(typeof(ShipmentLoad).FullName);

        Assert.NotNull(load.FindProperty(nameof(ShipmentLoad.TransshippedQuantity)));
        Assert.NotNull(load.FindProperty(nameof(ShipmentLoad.AvailableQuantity)));
    }

    /// <summary>
    /// ⚠️ [NotMapped] some do EDM: sem AddProperty explícito, $select=ShrinkageQuantity devolve
    /// 400 e a tela não consegue mostrar a quebra de transporte na linha do transbordo.
    /// </summary>
    [Fact]
    public void Exposes_the_computed_shrinkage_quantity()
    {
        var transshipment =
            (IEdmStructuredType)Model().FindDeclaredType(typeof(ShipmentLoadTransshipment).FullName);

        Assert.NotNull(transshipment.FindProperty(nameof(ShipmentLoadTransshipment.ShrinkageQuantity)));
    }

    [Fact]
    public void Start_action_takes_the_expected_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentStart");

        var names = action.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("LoadKey", names);
        Assert.Contains("WarehouseCode", names);
        Assert.Contains("TransshipmentDate", names);
        Assert.Contains("Comments", names);
    }

    /// <summary>
    /// ⚠️ Data como string, NUNCA Edm.Date: em parâmetro de action já custou uma sessão aqui —
    /// ver ShipmentLoadActionParameters.TryParseDate.
    /// </summary>
    [Fact]
    public void Start_action_declares_the_date_as_string()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentStart");

        Assert.Equal(
            "Edm.String",
            action.Parameters.Single(p => p.Name == "TransshipmentDate").Type.Definition.FullTypeName());
    }

    /// <summary>
    /// LoadKey, WarehouseCode e TransshipmentDate são obrigatórios; só Comments pode faltar.
    /// </summary>
    [Fact]
    public void Start_action_only_comments_is_optional()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentStart");

        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "LoadKey"));
        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "WarehouseCode"));
        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "TransshipmentDate"));

        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Comments"));
    }

    [Fact]
    public void RegisterEntry_action_takes_the_expected_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentRegisterEntry");

        var names = action.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("Key", names);
        Assert.Contains("EntryDate", names);
        Assert.Contains("GrossWeight", names);
        Assert.Contains("ReceiptStorageTransactionKey", names);
    }

    /// <summary>
    /// ⚠️ Peso como Edm.Double, NUNCA Edm.Decimal: decimal faz o cliente serializar o número
    /// como string e o backend devolve 400 que não nomeia o campo. Não existe parâmetro de tara
    /// nesta action — a entrada do transbordo tem um peso único (GrossWeight).
    /// </summary>
    [Fact]
    public void RegisterEntry_action_declares_the_weight_as_double()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentRegisterEntry");

        Assert.Equal(
            "Edm.Double",
            action.Parameters.Single(p => p.Name == "GrossWeight").Type.Definition.FullTypeName());
        Assert.Equal(
            "Edm.String",
            action.Parameters.Single(p => p.Name == "EntryDate").Type.Definition.FullTypeName());
        Assert.Equal(
            "Edm.Guid",
            action.Parameters.Single(p => p.Name == "ReceiptStorageTransactionKey")
                .Type.Definition.FullTypeName());
    }

    /// <summary>
    /// Key e EntryDate são obrigatórios; GrossWeight e ReceiptStorageTransactionKey podem faltar
    /// — armazém próprio deriva o peso do romaneio de Entrada em Armazenagem, e armazém de
    /// terceiro não tem romaneio prévio a apontar.
    /// </summary>
    [Fact]
    public void RegisterEntry_action_lets_the_optional_parameters_be_omitted()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentRegisterEntry");

        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Key"));
        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "EntryDate"));

        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "GrossWeight"));
        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "ReceiptStorageTransactionKey"));
    }

    [Fact]
    public void Reverse_action_takes_the_expected_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentReverse");

        var names = action.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("Key", names);
        Assert.Contains("Reason", names);

        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Key"));
        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Reason"));
    }

    /// <summary>
    /// GAC-1181: o parâmetro novo que expõe o papel do transbordo na vinculação de saída — sem
    /// ele, todo vínculo caía no caminho comum e a saída do transbordo nunca "fechava" a linha.
    /// Optional porque o vínculo comum (sem transbordo) continua existindo.
    /// </summary>
    [Fact]
    public void AttachTransactions_action_declares_the_transshipment_key_as_optional_guid()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsAttachTransactions");

        var parameter = action.Parameters.Single(p => p.Name == "TransshipmentKey");

        Assert.Equal("Edm.Guid", parameter.Type.Definition.FullTypeName());
        Assert.IsAssignableFrom<IEdmOptionalParameter>(parameter);
    }
}
