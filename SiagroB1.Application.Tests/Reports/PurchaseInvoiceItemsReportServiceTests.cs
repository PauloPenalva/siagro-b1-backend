using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class PurchaseInvoiceItemsReportServiceTests
{
    [Fact]
    public async Task BuildRows_ItemWithoutProductGoesToItsOwnGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", items:
            [PurchaseItem("10001", "SOJA"), PurchaseItem(null, "SOJA DO XML", uom: null), PurchaseItem(null, null, uom: null)]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "SOJA (10001) - TN", "SOJA DO XML", "Sem produto vinculado" },
            rows.Select(r => r.Group).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("ItemCode")]
    [InlineData("IssuerType")]
    [InlineData("ContractCode")]
    [InlineData("Cfop")]
    public async Task BuildRows_EachFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "CC-1", CardCode = "F001", ItemCode = "10001", UnitOfMeasureCode = "TN", HarvestSeasonCode = "2526", DeliveryLocationCode = "LOC1",
            Status = ContractStatus.Approved, CreationDate = Jul01,
        };
        db.Context.PurchaseContracts.Add(contract);
        var match = PurchaseItem();
        match.Cfop = "1101";
        match.PurchaseContractKey = contract.Key;
        var other = PurchaseItem(itemCode: "99999");
        other.Cfop = "2101";
        db.Context.PurchaseInvoices.Add(Purchase("MATCH", items: [match]));
        db.Context.PurchaseInvoices.Add(Purchase("OTHER", issuer: DocumentIssuerType.Own, items: [other]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "ItemCode": request.ItemCode = "10001"; break;
            case "IssuerType": request.IssuerType = DocumentIssuerType.ThirdParty; break;
            case "ContractCode": request.ContractCode = "CC-1"; break;
            case "Cfop": request.Cfop = "1101"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_FormatsTheLine()
    {
        var db = TestDb.CreateUnitOfWork();
        var item = PurchaseItem(quantity: 4m, unitPrice: 25m);
        item.Cfop = "1101";
        item.UsageName = "COMPRA PARA COMERCIALIZAÇÃO";
        item.IcmsValue = 3m;
        db.Context.PurchaseInvoices.Add(Purchase("8", items: [item]));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("(F001) PRODUTOR RURAL", row.Partner);
        Assert.Equal("1101", row.Cfop);
        Assert.Equal("COMPRA PARA COMERCIALIZAÇÃO", row.Usage);
        Assert.Equal(100m, row.Total);
        Assert.Equal(3m, row.Icms);
        Assert.Equal("", row.Contract);
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new PurchaseInvoiceItemsReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("PurchaseInvoiceItems.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static PurchaseInvoiceItemsReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static PurchaseInvoiceItemsRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
