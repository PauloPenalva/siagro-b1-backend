using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Sap;

/// <summary>
/// GAC-1226: a NF de devolução/complemento não se vincula mais 1:1 à nota de crédito do SAP; as operações e as
/// colunas desse vínculo saíram do EDM e do documento de saída.
/// </summary>
public class SapInvoiceLinkEdmTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ShipmentLoadsLinkSapCreditMemo")]
    [InlineData("ShipmentLoadsUnlinkSapCreditMemo")]
    [InlineData("ShipmentLoadsGetLinkableSapCreditMemos")]
    public void Credit_memo_link_routes_are_gone(string name) =>
        Assert.DoesNotContain(Model().SchemaElements.OfType<IEdmOperation>(), o => o.Name == name);

    [Fact]
    public void Link_action_has_the_optional_confirm_divergence_parameter()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(o => o.Name == "ShipmentLoadsLinkSapInvoice");
        var parameter = action.FindParameter("ConfirmDivergence");

        Assert.NotNull(parameter);
        Assert.True(parameter.Type.IsBoolean());
        Assert.IsAssignableFrom<IEdmOptionalParameter>(parameter);
    }

    [Fact]
    public void Preview_function_exists_with_its_parameters()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(o => o.Name == "ShipmentLoadsGetSapLinkPreview");

        Assert.NotNull(function.FindParameter("Key"));
        Assert.NotNull(function.FindParameter("SalesShipmentReleaseKey"));
        Assert.True(function.FindParameter("SapDocEntries")!.Type.IsString());
        Assert.Equal("SiagroB1.Domain.Dtos.SapLinkPreviewDto", function.GetReturn().Type.FullName());
    }

    [Fact]
    public void Sales_invoice_no_longer_exposes_the_credit_memo_link() =>
        Assert.Null(typeof(SalesInvoice).GetProperty("SapCreditMemoDocEntry"));
}
