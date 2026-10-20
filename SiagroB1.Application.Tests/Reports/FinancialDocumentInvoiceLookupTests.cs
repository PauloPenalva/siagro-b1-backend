using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.Reports;

public class FinancialDocumentInvoiceLookupTests
{
    private static FinancialDocument Doc(FinancialDocumentOrigin origin, FinancialDocumentNature nature = FinancialDocumentNature.Firm,
        Guid? purchaseInvoice = null, Guid? salesInvoice = null, Guid? originKey = null) => new()
    {
        Key = Guid.NewGuid(), Code = "T", CardCode = "C1", Nature = nature, OriginType = origin, OriginKey = originKey,
        PurchaseInvoiceKey = purchaseInvoice, SalesInvoiceKey = salesInvoice,
        DocumentDate = new DateTime(2026, 10, 1), DueDate = new DateTime(2026, 10, 1), NetAmount = 10m
    };

    [Fact]
    public async Task Maps_invoice_number_and_series_by_origin()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "8001", TaxDocumentSeries = "1" };
        var other = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "77", TaxDocumentSeries = "U" };
        var unnumbered = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = " ", TaxDocumentSeries = "2" };
        var sales = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "293", TaxDocumentSeries = "3" };
        var legacyNoContract = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "294", TaxDocumentSeries = "3" };
        db.Context.PurchaseInvoices.AddRange(purchase, other, unnumbered);
        db.Context.SalesInvoices.AddRange(sales, legacyNoContract);
        await db.Context.SaveChangesAsync();

        var firmPurchase = Doc(FinancialDocumentOrigin.PurchaseInvoice, purchaseInvoice: purchase.Key, originKey: Guid.NewGuid());
        var firmSales = Doc(FinancialDocumentOrigin.SalesInvoice, salesInvoice: sales.Key, originKey: Guid.NewGuid());
        var otherPurchase = Doc(FinancialDocumentOrigin.OtherPurchaseInvoice, purchaseInvoice: other.Key, originKey: other.Key);
        // Sem a chave explícita: cai no OriginKey, que nestas origens é a chave do documento.
        var noContractByOrigin = Doc(FinancialDocumentOrigin.SalesInvoiceNoContract, originKey: legacyNoContract.Key);
        var provisional = Doc(FinancialDocumentOrigin.PurchaseContractPriceFixation, FinancialDocumentNature.Provisional,
            originKey: purchase.Key);
        var noNumber = Doc(FinancialDocumentOrigin.PurchaseInvoice, purchaseInvoice: unnumbered.Key);
        var missing = Doc(FinancialDocumentOrigin.PurchaseInvoice, purchaseInvoice: Guid.NewGuid());

        var lookup = await FinancialDocumentInvoiceLookup.LoadAsync(db.Context,
            [firmPurchase, firmSales, otherPurchase, noContractByOrigin, provisional, noNumber, missing], CancellationToken.None);

        Assert.Equal(new InvoiceNumberSeries("8001", "1"), lookup.For(firmPurchase));
        Assert.Equal(new InvoiceNumberSeries("293", "3"), lookup.For(firmSales));
        Assert.Equal(new InvoiceNumberSeries("77", "U"), lookup.For(otherPurchase));
        Assert.Equal(new InvoiceNumberSeries("294", "3"), lookup.For(noContractByOrigin));
        Assert.Equal(InvoiceNumberSeries.Empty, lookup.For(provisional));
        Assert.Equal(InvoiceNumberSeries.Empty, lookup.For(noNumber));
        Assert.Equal(InvoiceNumberSeries.Empty, lookup.For(missing));
        Assert.Equal(("", ""), (InvoiceNumberSeries.Empty.Number, InvoiceNumberSeries.Empty.Series));
    }
}
