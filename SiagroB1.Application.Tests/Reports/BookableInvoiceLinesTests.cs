using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Services.Fiscal;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class BookableInvoiceLinesTests
{
    private static SalesInvoice S(string n, Action<SalesInvoice>? change = null)
    {
        var s = Sale(n);
        s.NfeStatus = NfeStatus.Authorized;
        change?.Invoke(s);
        return s;
    }

    private static PurchaseInvoice P(string n, Action<PurchaseInvoice>? change = null)
    {
        var p = Purchase(n);
        change?.Invoke(p);
        return p;
    }

    [Fact]
    public async Task Sales_KeepsOnlyBookable()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.AddRange(
            S("OK"),
            S("RET", s => s.InvoiceStatus = InvoiceStatus.Returned),
            S("OUTRO", s => { s.TaxDocumentKind = TaxDocumentKind.Other; s.NfeStatus = NfeStatus.None; }),
            S("PEND", s => s.InvoiceStatus = InvoiceStatus.Pending),
            S("CANC", s => s.InvoiceStatus = InvoiceStatus.Cancelled),
            S("SEMSTATUS", s => s.InvoiceStatus = null),
            S("SEMDOC", s => s.WithoutTaxDocument = true),
            S("CTE", s => s.TaxDocumentKind = TaxDocumentKind.Cte),
            S("NFCANC", s => s.NfeStatus = NfeStatus.Cancelled),
            S("NFDEN", s => s.NfeStatus = NfeStatus.Denied),
            S("NFINUT", s => s.NfeStatus = NfeStatus.Voided),
            S("NAOAUT", s => s.NfeStatus = NfeStatus.None));
        await db.Context.SaveChangesAsync();

        var kept = BookableInvoiceLines.Sales(db.Context.SalesInvoicesItems).Select(i => i.SalesInvoice!.InvoiceNumber).ToList();
        var unauthorized = BookableInvoiceLines.UnauthorizedOwnSales(db.Context.SalesInvoicesItems).Select(i => i.SalesInvoice!.InvoiceNumber).ToList();

        Assert.Equal(new[] { "OK", "OUTRO", "RET" }, kept.Order());
        Assert.Equal(new[] { "NAOAUT" }, unauthorized);
    }

    [Fact]
    public async Task Purchases_KeepsOnlyBookable()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.AddRange(
            P("OK"),
            P("RET", p => p.InvoiceStatus = InvoiceStatus.Returned),
            P("TERCEIRO", p => { p.TaxDocumentKind = TaxDocumentKind.Nfe; p.NfeStatus = NfeStatus.None; }),
            P("PROPRIA", p => { p.IssuerType = DocumentIssuerType.Own; p.NfeStatus = NfeStatus.Authorized; }),
            P("PROPRIA-OUTRO", p => { p.IssuerType = DocumentIssuerType.Own; p.TaxDocumentKind = TaxDocumentKind.Other; p.NfeStatus = NfeStatus.None; }),
            P("PROPRIA-NAOAUT", p => { p.IssuerType = DocumentIssuerType.Own; p.NfeStatus = NfeStatus.None; }),
            P("PEND", p => p.InvoiceStatus = InvoiceStatus.Pending),
            P("CANC", p => p.InvoiceStatus = InvoiceStatus.Cancelled),
            P("CTE", p => p.TaxDocumentKind = TaxDocumentKind.Cte),
            P("NFCANC", p => p.NfeStatus = NfeStatus.Cancelled),
            P("NFDEN", p => p.NfeStatus = NfeStatus.Denied),
            P("NFINUT", p => p.NfeStatus = NfeStatus.Voided));
        await db.Context.SaveChangesAsync();

        var kept = BookableInvoiceLines.Purchases(db.Context.PurchaseInvoicesItems).Select(i => i.PurchaseInvoice!.InvoiceNumber).ToList();
        var unauthorized = BookableInvoiceLines.UnauthorizedOwnPurchases(db.Context.PurchaseInvoicesItems).Select(i => i.PurchaseInvoice!.InvoiceNumber).ToList();

        Assert.Equal(new[] { "OK", "PROPRIA", "PROPRIA-OUTRO", "RET", "TERCEIRO" }, kept.Order());
        Assert.Equal(new[] { "PROPRIA-NAOAUT" }, unauthorized);
    }
}
