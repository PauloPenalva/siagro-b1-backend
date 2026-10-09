using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesInvoiceItemsReportServiceTests
{
    [Fact]
    public async Task BuildRows_NullNameAndNamedLinesOfSameCodeFormOneGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", new DateTime(2026, 7, 10), items: [SaleItem("1", null)]));
        db.Context.SalesInvoices.Add(Sale("2", new DateTime(2026, 7, 11), items: [SaleItem("1", "TRIGO EM GRÃOS")]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "TRIGO EM GRÃOS (1) - TN", "TRIGO EM GRÃOS (1) - TN" }, rows.Select(r => r.Group));
    }

    [Fact]
    public async Task BuildRows_DifferentSpellingsOfSameCodeUseTheMostFrequentName()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", new DateTime(2026, 7, 10), items: [SaleItem("1", "TRIGO EM GRAOS")]));
        db.Context.SalesInvoices.Add(Sale("2", new DateTime(2026, 7, 11), items: [SaleItem("1", "TRIGO EM GRÃOS"), SaleItem("1", "TRIGO EM GRÃOS")]));
        db.Context.SalesInvoices.Add(Sale("3", new DateTime(2026, 7, 12), items: [SaleItem("1", "TRIGO EM GRAOS TIPO 1")]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal("TRIGO EM GRÃOS (1) - TN", r.Group));
    }

    [Fact]
    public async Task BuildRows_SameCodeDifferentUnitFormTwoGroups()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", new DateTime(2026, 7, 10), items: [SaleItem("1", "TRIGO", uom: "KG"), SaleItem("1", null, uom: "TN")]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "TRIGO (1) - KG", "TRIGO (1) - TN" }, rows.Select(r => r.Group));
    }

    [Fact]
    public async Task BuildRows_OneRowPerItemGroupedByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("2", new DateTime(2026, 7, 20),
            items: [SaleItem("10001", "SOJA", 5m), SaleItem("10002", "MILHO", 3m)]));
        db.Context.SalesInvoices.Add(Sale("1", new DateTime(2026, 7, 10),
            items: [SaleItem("10001", "SOJA", 7m), SaleItem("10001", "SOJA", 900m, uom: "KG")]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (10002) - TN", "SOJA (10001) - KG", "SOJA (10001) - TN", "SOJA (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { 3m, 900m, 7m, 5m }, rows.Select(r => r.Quantity));
    }

    [Fact]
    public async Task BuildRows_ItemFilterRestrictsLinesNotOnlyDocuments()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", items: [SaleItem("10001", "SOJA"), SaleItem("10002", "MILHO")]));
        await Save(db);

        var request = Request();
        request.ItemCode = "10002";
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MILHO (10002) - TN" }, rows.Select(r => r.Group));
    }

    [Theory]
    [InlineData("Cfop")]
    [InlineData("ContractCode")]
    [InlineData("InvoiceType")]
    [InlineData("Status")]
    public async Task BuildRows_EachLineFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "CV-1", CardCode = "C001", ItemCode = "10001", UnitOfMeasureCode = "TN", HarvestSeasonCode = "2526",
            Status = ContractStatus.Approved, CreationDate = Jul01,
        };
        db.Context.SalesContracts.Add(contract);
        var match = SaleItem();
        match.Cfop = "5101";
        match.SalesContractKey = contract.Key;
        var other = SaleItem();
        other.Cfop = "6101";
        db.Context.SalesInvoices.Add(Sale("MATCH", items: [match]));
        db.Context.SalesInvoices.Add(Sale("OTHER", status: InvoiceStatus.Cancelled, type: SalesInvoiceType.Return, items: [other]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "Cfop": request.Cfop = "5101"; request.Statuses = [InvoiceStatus.Confirmed, InvoiceStatus.Cancelled]; break;
            case "ContractCode": request.ContractCode = "CV-1"; request.Statuses = [InvoiceStatus.Confirmed, InvoiceStatus.Cancelled]; break;
            case "InvoiceType": request.InvoiceType = SalesInvoiceType.Normal; request.Statuses = [InvoiceStatus.Confirmed, InvoiceStatus.Cancelled]; break;
            case "Status": break; // padrão já exclui o cancelado
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_FormatsTheLine()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "CV-9", CardCode = "C001", ItemCode = "10001", UnitOfMeasureCode = "TN", HarvestSeasonCode = "2526",
            Status = ContractStatus.Approved, CreationDate = Jul01,
        };
        db.Context.SalesContracts.Add(contract);
        var item = SaleItem(quantity: 10m, unitPrice: 100m);
        item.Cfop = "5101";
        item.UsageCode = 21;
        item.FreightValue = 10m;
        item.IcmsValue = 12m;
        item.PisValue = 1m;
        item.CofinsValue = 2m;
        item.SalesContractKey = contract.Key;
        var invoice = Sale("1", items: [item]);
        invoice.TaxDocumentNumber = "321";
        invoice.TaxDocumentSeries = "1";
        db.Context.SalesInvoices.Add(invoice);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("15/07/2026", row.IssueDate);
        Assert.Equal("321/1", row.DocumentNumber);
        Assert.Equal("(C001) COOPERATIVA CENTRAL", row.Partner);
        Assert.Equal("5101", row.Cfop);
        Assert.Equal("21", row.Usage);
        Assert.Equal(10m, row.Quantity);
        Assert.Equal("TN", row.UnitOfMeasure);
        Assert.Equal(100m, row.UnitPrice);
        Assert.Equal(1000m, row.Total);
        Assert.Equal(12m, row.Icms);
        Assert.Equal(1m, row.Pis);
        Assert.Equal(2m, row.Cofins);
        Assert.Equal(1010m, row.GrandTotal);
        Assert.Equal("CV-9", row.Contract);
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new SalesInvoiceItemsReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("SalesInvoiceItems.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static SalesInvoiceItemsReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static SalesInvoiceItemsRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
