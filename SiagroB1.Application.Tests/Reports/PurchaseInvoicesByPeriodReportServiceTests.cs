using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class PurchaseInvoicesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_FiltersByIssueDateIncludingTheLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.PurchaseInvoices.Add(Purchase("2", new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "1" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("OK"));
        db.Context.PurchaseInvoices.Add(Purchase("CANC", status: InvoiceStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "OK" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("CardCode")]
    [InlineData("ItemCode")]
    [InlineData("InvoiceType")]
    [InlineData("IssuerType")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("MATCH"));
        db.Context.PurchaseInvoices.Add(Purchase("OTHER", type: PurchaseInvoiceType.Return,
            issuer: DocumentIssuerType.Own, branchCode: "99", cardCode: "F999",
            items: [PurchaseItem(itemCode: "99999")]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "CardCode": request.CardCode = "F001"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "InvoiceType": request.InvoiceType = PurchaseInvoiceType.Normal; break;
            case "IssuerType": request.IssuerType = DocumentIssuerType.ThirdParty; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsAndTotals()
    {
        var db = TestDb.CreateUnitOfWork();
        var item = PurchaseItem(quantity: 10m, unitPrice: 100m);
        item.FreightValue = 30m;
        item.IcmsValue = 12m;
        var invoice = Purchase("7", items: [item], issuer: DocumentIssuerType.Own);
        invoice.TaxDocumentNumber = "55";
        invoice.TaxDocumentSeries = "9";
        invoice.TotalDocumentValue = 1030m;
        db.Context.PurchaseInvoices.Add(invoice);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("55/9", row.DocumentNumber);
        Assert.Equal("15/07/2026", row.IssueDate);
        Assert.Equal("16/07/2026", row.PostingDate);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Issuer);
        Assert.Equal("Normal", row.Type);
        Assert.Equal("Própria", row.IssuerType);
        Assert.Equal(1030m, row.DeclaredValue);
        Assert.Equal(1000m, row.ProductsTotal);
        Assert.Equal(30m, row.Freight);
        Assert.Equal(12m, row.Taxes);
        Assert.Equal(1030m, row.GrandTotal);
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new PurchaseInvoicesByPeriodReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("PurchaseInvoicesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static PurchaseInvoicesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static PurchaseInvoicesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
