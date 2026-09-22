using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1181 fase 2, Task 8 — o EDM é contrato com a tela: action ou propriedade que não está
/// aqui devolve 404/400 no navegador, e nenhum teste de serviço acusa. Molde de
/// <see cref="ShipmentLoadTransshipmentEdmModelTests"/>.
/// </summary>
public class ShipmentLoadTransshipmentPhase2EdmTests
{
    // Mesmo helper de ShipmentLoadTransshipmentEdmModelTests: o EDM real, montado pelo
    // ConfigureODataEntities que o Program usa.
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Fact]
    public void Declares_the_attach_lot_exit_action()
    {
        Assert.Contains(
            Model().SchemaElements.OfType<IEdmAction>(),
            a => a.Name == "ShipmentLoadsTransshipmentAttachLotExit");
    }

    [Fact]
    public void AttachLotExit_action_takes_the_expected_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentAttachLotExit");

        var names = action.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("Key", names);
        Assert.Contains("LotExitStorageTransactionKey", names);
    }

    [Fact]
    public void AttachLotExit_action_declares_both_parameters_as_guid()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentAttachLotExit");

        Assert.Equal(
            "Edm.Guid",
            action.Parameters.Single(p => p.Name == "Key").Type.Definition.FullTypeName());
        Assert.Equal(
            "Edm.Guid",
            action.Parameters.Single(p => p.Name == "LotExitStorageTransactionKey")
                .Type.Definition.FullTypeName());
    }

    /// <summary>
    /// Os dois são obrigatórios — sem o romaneio de saída não existe o que vincular, e sem o
    /// transbordo não existe onde vincular.
    /// </summary>
    [Fact]
    public void AttachLotExit_action_requires_both_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsTransshipmentAttachLotExit");

        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Key"));
        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "LotExitStorageTransactionKey"));
    }

    /// <summary>
    /// Natureza do lote (GAC-1181 fase 2, Task 1): é campo persistido, não [NotMapped] — mas o
    /// teste prova o que chega de fato na tela, não como a propriedade está anotada no código.
    /// </summary>
    [Fact]
    public void StorageAddress_exposes_the_nature()
    {
        var storageAddress =
            (IEdmStructuredType)Model().FindDeclaredType(typeof(StorageAddress).FullName);

        Assert.NotNull(storageAddress.FindProperty(nameof(StorageAddress.Nature)));
    }
}
