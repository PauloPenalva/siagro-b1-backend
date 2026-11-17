using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Sap;

/// <summary>
/// O EDM real das operações da NF de devolução do SAP (GAC-1226): a tela da carga as chama pelo nome e pelo tipo
/// dos parâmetros, e o documento de saída expõe as duas colunas do vínculo.
/// </summary>
public class SapCreditMemoLinkEdmTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    private static string[] Parameters(IEdmOperation operation) =>
        operation.Parameters.Select(p => $"{p.Name}:{p.Type.FullName()}").ToArray();

    [Fact]
    public void Linkable_credit_memos_is_a_function_keyed_by_load_return_document_and_period()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "ShipmentLoadsGetLinkableSapCreditMemos");

        Assert.Equal(
            ["Key:Edm.Guid", "SalesInvoiceKey:Edm.Guid", "DateFrom:Edm.String", "DateTo:Edm.String"],
            Parameters(function));
        var row = Assert.IsAssignableFrom<IEdmComplexType>(
            ((IEdmCollectionType)function.ReturnType.Definition).ElementType.Definition);
        foreach (var property in new[]
                 {
                     "DocEntry", "DocNum", "DocDate", "TaxDocumentNumber", "TaxDocumentSeries", "Quantity", "LineTotal",
                     "Cfop", "UsageName", "MatchesOrigin", "QuantityMatches", "BaseInvoiceDocEntry",
                 })
            Assert.Contains(row.StructuralProperties(), p => p.Name == property);
    }

    [Fact]
    public void Link_takes_the_load_the_return_document_and_the_sap_doc_entry()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "ShipmentLoadsLinkSapCreditMemo");

        Assert.False(action.IsBound);
        Assert.Equal(["Key:Edm.Guid", "SalesInvoiceKey:Edm.Guid", "SapDocEntry:Edm.Int32"], Parameters(action));
    }

    [Fact]
    public void Unlink_takes_the_load_and_the_return_document()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "ShipmentLoadsUnlinkSapCreditMemo");

        Assert.False(action.IsBound);
        Assert.Equal(["Key:Edm.Guid", "SalesInvoiceKey:Edm.Guid"], Parameters(action));
    }

    [Fact]
    public void Sales_invoice_exposes_the_credit_memo_link_columns()
    {
        var invoice = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == "SalesInvoice");

        Assert.Equal("Edm.Int32", invoice.FindProperty("SapCreditMemoDocEntry").Type.FullName());
        Assert.Equal("Edm.Int32", invoice.FindProperty("SapCreditMemoDocNum").Type.FullName());
    }
}
