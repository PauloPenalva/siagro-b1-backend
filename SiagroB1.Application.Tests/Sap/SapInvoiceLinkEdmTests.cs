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
    public void Sales_invoice_no_longer_exposes_the_credit_memo_link() =>
        Assert.Null(typeof(SalesInvoice).GetProperty("SapCreditMemoDocEntry"));
}
