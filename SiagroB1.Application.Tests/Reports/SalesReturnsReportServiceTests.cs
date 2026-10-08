using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesReturnsReportServiceTests
{
    [Fact]
    public async Task BuildRows_BringsBothSourcesLinkedToTheOriginalInvoice()
    {
        var db = TestDb.CreateUnitOfWork();
        var originalItem = SaleItem(quantity: 30m);
        var original = Sale("100", new DateTime(2026, 6, 20), items: [originalItem]);
        original.TaxDocumentNumber = "5000";
        original.TaxDocumentSeries = "1";
        db.Context.SalesInvoices.Add(original);

        var ownItem = SaleItem(quantity: 5m, unitPrice: 100m);
        ownItem.SalesInvoiceItemOriginKey = originalItem.Key;
        db.Context.SalesInvoices.Add(Sale("101", new DateTime(2026, 7, 10), type: SalesInvoiceType.Return, items: [ownItem]));

        var customerItem = PurchaseItem(quantity: 2m, unitPrice: 100m);
        customerItem.SalesInvoiceItemKey = originalItem.Key;
        db.Context.PurchaseInvoices.Add(Purchase("900", new DateTime(2026, 7, 12), type: PurchaseInvoiceType.Return,
            cardCode: "C001", cardName: "COOPERATIVA CENTRAL", items: [customerItem]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Própria", "Cliente" }, rows.Select(r => r.Source));
        Assert.All(rows, r => Assert.Equal("5000/1 de 20/06/2026", r.Origin));
        Assert.Equal(new[] { 5m, 2m }, rows.Select(r => r.Quantity));
        Assert.Equal(new[] { 500m, 200m }, rows.Select(r => r.Value));
        Assert.All(rows, r => Assert.Equal("(C001) COOPERATIVA CENTRAL", r.Customer));
    }

    [Fact]
    public async Task BuildRows_IgnoresNonReturnsAndOwnPurchaseReturns()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("NORMAL"));
        db.Context.PurchaseInvoices.Add(Purchase("ENTRADA"));
        // Devolução de COMPRA (emissão própria) não é devolução de venda.
        db.Context.PurchaseInvoices.Add(Purchase("DEV-COMPRA", type: PurchaseInvoiceType.Return, issuer: DocumentIssuerType.Own));
        await Save(db);

        Assert.Empty(await Service(db).BuildRowsAsync(Request()));
    }

    [Fact]
    public async Task BuildRows_WithoutLinkPrintsADash()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(InvoiceReportText.NoOrigin, r.Origin));
    }

    [Theory]
    [InlineData(SalesReturnSource.Own, "101")]
    [InlineData(SalesReturnSource.Customer, "900")]
    public async Task BuildRows_SourceFilter(SalesReturnSource source, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return));
        await Save(db);

        var request = Request();
        request.Source = source;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { expected }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelledReturns()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return, status: InvoiceStatus.Cancelled));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return, status: InvoiceStatus.Cancelled));
        await Save(db);

        Assert.Empty(await Service(db).BuildRowsAsync(Request()));
    }

    [Fact]
    public async Task BuildRows_OrdersByCustomerThenDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("B2", new DateTime(2026, 7, 20), type: SalesInvoiceType.Return, cardCode: "C2", cardName: "BETA"));
        db.Context.SalesInvoices.Add(Sale("A1", new DateTime(2026, 7, 25), type: SalesInvoiceType.Return, cardCode: "C1", cardName: "ALFA"));
        db.Context.SalesInvoices.Add(Sale("B1", new DateTime(2026, 7, 5), type: SalesInvoiceType.Return, cardCode: "C2", cardName: "BETA"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "A1", "B1", "B2" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new SalesReturnsReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("SalesReturns.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static SalesReturnsReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static SalesReturnsRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
