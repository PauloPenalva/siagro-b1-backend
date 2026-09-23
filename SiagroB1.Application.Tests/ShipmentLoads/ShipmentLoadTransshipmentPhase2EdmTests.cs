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

    /// <summary>
    /// GAC-1181 fase 2 (redesenho): a action "ShipmentLoadsTransshipmentAttachLotExit" saiu do EDM
    /// — o gatilho da liberação deixou de ser um botão da carga e passou a ser a confirmação do
    /// romaneio de saída na pesagem. O SERVIÇO (<c>ShipmentLoadsTransshipmentAttachLotExitService</c>)
    /// permanece, chamado agora só internamente; esta classe prova que o ponto de entrada HTTP não
    /// existe mais.
    /// </summary>
    [Fact]
    public void Does_not_declare_the_attach_lot_exit_action_anymore()
    {
        Assert.DoesNotContain(
            Model().SchemaElements.OfType<IEdmAction>(),
            a => a.Name == "ShipmentLoadsTransshipmentAttachLotExit");
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
