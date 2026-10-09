using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesInvoicesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", new DateTime(2026, 7, 31, 23, 45, 0)));
        db.Context.SalesInvoices.Add(Sale("2", new DateTime(2026, 8, 1, 0, 5, 0)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "1" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelledAndTreatsNullStatusAsPending()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("OK", status: InvoiceStatus.Confirmed));
        db.Context.SalesInvoices.Add(Sale("NULL", status: null));
        db.Context.SalesInvoices.Add(Sale("CANC", status: InvoiceStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "NULL", "OK" }, rows.Select(r => r.InternalNumber).Order());
        Assert.Equal("Pendente", rows.Single(r => r.InternalNumber == "NULL").Status);
    }

    [Fact]
    public async Task BuildRows_ExplicitCancelledFilterReturnsOnlyCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("OK"));
        db.Context.SalesInvoices.Add(Sale("CANC", status: InvoiceStatus.Cancelled));
        await Save(db);

        var request = Request();
        request.Statuses = [InvoiceStatus.Cancelled];
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CANC" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("CardCode")]
    [InlineData("ItemCode")]
    [InlineData("InvoiceType")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("MATCH"));
        db.Context.SalesInvoices.Add(Sale("OTHER", type: SalesInvoiceType.Return, branchCode: "99", cardCode: "C999",
            items: [SaleItem(itemCode: "99999")]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "CardCode": request.CardCode = "C001"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "InvoiceType": request.InvoiceType = SalesInvoiceType.Normal; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_NfeFilterAppliesInStandaloneAndIsIgnoredInSapB1()
    {
        var db = TestDb.CreateUnitOfWork();
        var authorized = Sale("AUT");
        authorized.NfeStatus = NfeStatus.Authorized;
        db.Context.SalesInvoices.Add(authorized);
        db.Context.SalesInvoices.Add(Sale("NONE"));
        await Save(db);

        var request = Request();
        request.NfeStatuses = [NfeStatus.Authorized];

        Assert.Equal(new[] { "AUT" }, (await Service(db, "STANDALONE").BuildRowsAsync(request)).Select(r => r.InternalNumber));
        Assert.Equal(2, (await Service(db, "SAPB1").BuildRowsAsync(request)).Count);
    }

    [Fact]
    public async Task BuildRows_TotalsMatchTheDomainProperties()
    {
        var db = TestDb.CreateUnitOfWork();
        var a = SaleItem(quantity: 10m, unitPrice: 100m);
        a.FreightValue = 50m;
        a.DiscountValue = 20m;
        a.IcmsValue = 12m;
        a.PisValue = 1.65m;
        a.CofinsValue = 7.6m;
        var b = SaleItem(quantity: 2m, unitPrice: 50m);
        var invoice = Sale("1", items: [a, b]);
        invoice.NetWeight = 12000m;
        invoice.TaxDocumentNumber = "000123";
        invoice.TaxDocumentSeries = "1";
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.SalesInvoices.Add(invoice);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("MATRIZ", row.Branch);
        Assert.Equal("000123/1", row.DocumentNumber);
        Assert.Equal("15/07/2026", row.IssueDate);
        Assert.Equal("(C001) COOPERATIVA CENTRAL", row.Customer);
        Assert.Equal("Normal", row.Type);
        Assert.Equal("Confirmado", row.Status);
        Assert.Equal(12000m, row.NetWeight);
        Assert.Equal(1100m, row.ProductsTotal);
        Assert.Equal(50m, row.Freight);
        Assert.Equal(20m, row.Discount);
        Assert.Equal(21.25m, row.Taxes);
        Assert.Equal(1130m, row.GrandTotal);
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenNumber()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("B", new DateTime(2026, 7, 10)));
        db.Context.SalesInvoices.Add(Sale("C", new DateTime(2026, 7, 5)));
        db.Context.SalesInvoices.Add(Sale("A", new DateTime(2026, 7, 10)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "C", "A", "B" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheModeAndFiltersToTheTemplate(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1"));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new SalesInvoicesByPeriodReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("SalesInvoicesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
        Assert.StartsWith("Emissão: 01/07/2026 a 31/07/2026", (string)recorder.LastParameters["pFilters"]);
    }

    private static SalesInvoicesByPeriodReportService Service(IUnitOfWork db, string erp = "STANDALONE") =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp(erp));

    private static SalesInvoicesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
