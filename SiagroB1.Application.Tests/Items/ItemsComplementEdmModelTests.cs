using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Items;

/// <summary>
/// O EDM real do serviço, montado pelo mesmo <c>ConfigureODataEntities</c> que o Program usa.
/// Trava que a function/action do complemento do item continuam expostas no metadata. Os dois
/// campos de <c>SalesShipmentReleaseAvailableDto</c> (CommercialUnitOfMeasureCode/CommercialPrice)
/// são cobertos pelos testes de <c>SalesShipmentReleasesGetAvailableServiceTests</c>, que
/// exercitam o valor real via EF Core — mais forte que checar o nome do tipo no EDM, que o
/// <c>ODataConventionModelBuilder</c> não expõe de forma previsível para o retorno de uma function.
/// </summary>
public class ItemsComplementEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Fact]
    public void ItemsGetComplement_is_exposed_as_a_function()
    {
        var function = Model()
            .SchemaElements
            .OfType<IEdmFunction>()
            .Single(f => f.Name == "ItemsGetComplement");

        var parameter = function.Parameters.Single(p => p.Name == "ItemCode");
        Assert.Equal("Edm.String", parameter.Type.FullName());
    }

    [Fact]
    public void ItemsSetComplement_is_exposed_as_an_action()
    {
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == "ItemsSetComplement");

        Assert.Contains(action.Parameters, p => p.Name == "ItemCode");
        Assert.Contains(action.Parameters, p => p.Name == "CommercialUnitOfMeasureCode");
        Assert.Contains(action.Parameters, p => p.Name == "CommercialFactor");
    }

    /// <summary>
    /// Limpar o complemento é operação válida: se estes dois deixarem de ser anuláveis no EDM, o
    /// UI5 leva 400 ao gravar o formulário vazio e a tela trava sem dizer por quê.
    /// </summary>
    [Fact]
    public void The_two_complement_fields_are_optional()
    {
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == "ItemsSetComplement");

        Assert.True(action.Parameters.Single(p => p.Name == "CommercialUnitOfMeasureCode").Type.IsNullable);
        Assert.True(action.Parameters.Single(p => p.Name == "CommercialFactor").Type.IsNullable);
    }

    /// <summary>
    /// Edm.Decimal aqui quebra a gravacao pela TELA e so pela tela: o UI5 v4 manda o numero como
    /// string e o OData recusa o corpo inteiro com "The parameters field is required", sem citar
    /// o campo. Por curl com JSON numerico passava, o que esconde o defeito.
    /// </summary>
    [Fact]
    public void CommercialFactor_is_a_double_never_a_decimal()
    {
        var action = Model()
            .SchemaElements
            .OfType<IEdmAction>()
            .Single(a => a.Name == "ItemsSetComplement");

        Assert.Equal("Edm.Double", action.Parameters.Single(p => p.Name == "CommercialFactor").Type.FullName());
    }
}
