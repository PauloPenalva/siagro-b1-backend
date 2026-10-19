using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Modelo dos firmes a receber (spec 2026-10-15): natureza nova, gates e índice de idempotência.</summary>
public class FinancialSalesReceivableModelTests
{
    private static FinancialDocument Document(FinancialDocumentNature nature, FinancialDocumentOrigin origin) => new()
    {
        Key = Guid.NewGuid(), Code = "FN000001", CardCode = "C0001", Direction = FinancialDirection.Receivable,
        Nature = nature, OriginType = origin, NetAmount = 100m, DueDate = new DateTime(2026, 12, 31),
    };

    [Fact]
    public void Sales_return_is_appended_with_a_fixed_number()
    {
        Assert.Equal(5, (int)FinancialDocumentNature.SalesReturn);
        Assert.Equal(4, (int)FinancialDocumentOrigin.SalesInvoice);
    }

    [Theory]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice, true, true)]
    [InlineData(FinancialDocumentNature.SalesReturn, FinancialDocumentOrigin.SalesInvoice, true, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation, false, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseInvoice, false, true)]
    [InlineData(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, false, false)]
    [InlineData(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, false, false)]
    [InlineData(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.SalesContractPriceFixation, false, false)]
    public void Gates_recognise_the_sales_firm_and_keep_the_purchase_ones(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, bool sales, bool contractFirm)
    {
        var document = Document(nature, origin);

        Assert.Equal(sales, document.IsSalesFirmReceivable);
        Assert.Equal(contractFirm, document.IsContractFirmDocument);
    }

    [Fact]
    public void Sales_firm_and_sales_return_are_blocked_for_direct_settlement()
    {
        Assert.True(Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice).IsBlockedForSettlement);
        Assert.True(Document(FinancialDocumentNature.SalesReturn, FinancialDocumentOrigin.SalesInvoice).IsBlockedForSettlement);
        // O título a receber do washout segue livre (regressão).
        Assert.False(Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout).IsBlockedForSettlement);
    }

    [Fact]
    public void Same_contract_compares_the_purchase_and_sales_pair()
    {
        var contract = Guid.NewGuid();
        var a = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice);
        var b = Document(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.SalesContractPriceFixation);
        a.SalesContractKey = contract;
        b.SalesContractKey = contract;
        Assert.True(a.HasSameContractAs(b));

        b.SalesContractKey = Guid.NewGuid();
        Assert.False(a.HasSameContractAs(b));

        // Compra × venda com a "mesma" chave nunca casa; sem contrato nenhum também não.
        b.SalesContractKey = null;
        b.PurchaseContractKey = contract;
        Assert.False(a.HasSameContractAs(b));
        a.SalesContractKey = null;
        b.PurchaseContractKey = null;
        Assert.False(a.HasSameContractAs(b));
    }

    [Fact]
    public void Firm_origin_index_includes_the_sales_contract_and_the_sales_return()
    {
        var unitOfWork = TestDb.CreateUnitOfWork();
        var context = unitOfWork.Context;
        var index = context.Model.FindEntityType(typeof(FinancialDocument))!.GetIndexes()
            .Single(i => i.Name == "IX_FINANCIAL_DOCUMENTS_FirmOrigin");

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[] { nameof(FinancialDocument.OriginType), nameof(FinancialDocument.OriginKey), nameof(FinancialDocument.SalesContractKey) },
            index.Properties.Select(p => p.Name).ToArray());
        Assert.Equal("[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL AND [OriginType] <> 9 AND [OriginType] <> 10", index.GetFilter());
    }

    [Fact]
    public void Edm_exposes_the_sales_invoice_key_but_not_the_gates()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var document = builder.GetEdmModel().EntityContainer.FindEntitySet("FinancialDocuments").EntityType;

        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.SalesInvoiceKey)));
        Assert.Null(document.FindProperty(nameof(FinancialDocument.IsSalesFirmReceivable)));
        Assert.Null(document.FindProperty(nameof(FinancialDocument.IsContractFirmDocument)));
    }
}
