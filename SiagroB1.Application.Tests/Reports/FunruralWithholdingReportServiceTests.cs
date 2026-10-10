using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class FunruralWithholdingReportServiceTests
{
    [Fact]
    public async Task Build_LineWithoutFunruralIsLeftOut()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("COM", items: [Withheld(1000m)]));
        db.Context.PurchaseInvoices.Add(Purchase("SEM", items: [PurchaseItem()]));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(new[] { "COM" }, data.Rows.Select(r => r.InternalNumber));
        Assert.Equal(1, data.ItemCount);
    }

    [Fact]
    public async Task Build_ItemRowCarriesRatesAndValues()
    {
        var db = TestDb.CreateUnitOfWork();
        var item = Withheld(1000m);
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [item]));
        await Save(db);

        var row = (await Service(db).BuildAsync(Request())).Rows.Single();

        Assert.Equal(FunruralRowKind.Item, row.Kind);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Partner);
        Assert.Equal("SOJA EM GRÃOS (10001) - TN", row.Description);
        Assert.Equal(1000m, row.Base);
        Assert.Equal("1,20", row.InssRate);
        Assert.Equal(12m, row.InssValue);
        Assert.Equal("0,10", row.RatRate);
        Assert.Equal(1m, row.RatValue);
        Assert.Equal("0,20", row.SenarRate);
        Assert.Equal(2m, row.SenarValue);
        Assert.Equal(15m, row.Total);
    }

    [Fact]
    public async Task Build_ReturnIssuedAfterThePeriodIsDeductedOnTheOriginLine()
    {
        var db = TestDb.CreateUnitOfWork();
        var bought = Withheld(1000m);
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [bought]));
        db.Context.PurchaseInvoices.Add(Return("DEV", new DateTime(2026, 8, 20), bought, 400m));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(
            new[] { FunruralRowKind.Item, FunruralRowKind.Return, FunruralRowKind.Net },
            data.Rows.Select(r => r.Kind));

        var ret = data.Rows[1];
        Assert.Equal("DEV", ret.InternalNumber);
        Assert.Equal("20/08/2026", ret.IssueDate);
        Assert.Equal(-400m, ret.Base);
        Assert.Equal(-4.8m, ret.InssValue);
        Assert.Equal(-0.4m, ret.RatValue);
        Assert.Equal(-0.8m, ret.SenarValue);
        Assert.Equal(-6m, ret.Total);
        Assert.Equal("", ret.InssRate);

        var net = data.Rows[2];
        Assert.Equal(600m, net.Base);
        Assert.Equal(7.2m, net.InssValue);
        Assert.Equal(9m, net.Total);
    }

    [Fact]
    public async Task Build_TwoPartialReturnsAreBothDeducted()
    {
        var db = TestDb.CreateUnitOfWork();
        var bought = Withheld(1000m);
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [bought]));
        db.Context.PurchaseInvoices.Add(Return("DEV1", new DateTime(2026, 7, 20), bought, 100m));
        db.Context.PurchaseInvoices.Add(Return("DEV2", new DateTime(2026, 7, 25), bought, 200m));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(new[] { "1", "DEV1", "DEV2", "" }, data.Rows.Select(r => r.InternalNumber));
        Assert.Equal(700m, data.Rows[^1].Base);
    }

    [Theory]
    [InlineData(InvoiceStatus.Cancelled, NfeStatus.Cancelled)]
    [InlineData(InvoiceStatus.Confirmed, NfeStatus.Cancelled)]
    [InlineData(InvoiceStatus.Confirmed, NfeStatus.Denied)]
    public async Task Build_CancelledReturnIsNotDeducted(InvoiceStatus status, NfeStatus nfe)
    {
        var db = TestDb.CreateUnitOfWork();
        var bought = Withheld(1000m);
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [bought]));
        var ret = Return("DEV", new DateTime(2026, 7, 20), bought, 400m);
        ret.InvoiceStatus = status;
        ret.NfeStatus = nfe;
        db.Context.PurchaseInvoices.Add(ret);
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(new[] { FunruralRowKind.Item }, data.Rows.Select(r => r.Kind));
        Assert.Equal(15m, data.Net.Total);
    }

    [Fact]
    public async Task Build_ReturnsAndCustomerReturnsAreNeverOriginLines()
    {
        var db = TestDb.CreateUnitOfWork();
        var bought = Withheld(1000m);
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [bought]));
        db.Context.PurchaseInvoices.Add(Return("DEV", new DateTime(2026, 7, 20), bought, 400m));
        db.Context.PurchaseInvoices.Add(Purchase("CLI", type: PurchaseInvoiceType.Return, items: [Withheld(500m)]));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(1, data.ItemCount);
        Assert.DoesNotContain(data.Rows, r => r.InternalNumber == "CLI");
        Assert.Single(data.Rows, r => r.InternalNumber == "DEV");
    }

    [Fact]
    public async Task Build_TotalsAreGrossMinusReturnsEqualsNet()
    {
        var db = TestDb.CreateUnitOfWork();
        var a = Withheld(1000m);
        var b = Withheld(2000m);
        db.Context.PurchaseInvoices.Add(Purchase("1", new DateTime(2026, 7, 10), items: [a]));
        db.Context.PurchaseInvoices.Add(Purchase("2", new DateTime(2026, 7, 11), items: [b]));
        db.Context.PurchaseInvoices.Add(Return("DEV", new DateTime(2026, 7, 20), a, 400m));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(3000m, data.Gross.Base);
        Assert.Equal(45m, data.Gross.Total);
        Assert.Equal(-400m, data.Returns.Base);
        Assert.Equal(-6m, data.Returns.Total);
        Assert.Equal(2600m, data.Net.Base);
        Assert.Equal(31.2m, data.Net.InssValue);
        Assert.Equal(2.6m, data.Net.RatValue);
        Assert.Equal(5.2m, data.Net.SenarValue);
        Assert.Equal(39m, data.Net.Total);
        Assert.Equal(new[] { "1", "DEV", "", "2" }, data.Rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task Build_PeriodFiltersTheEntryNotTheReturn()
    {
        var db = TestDb.CreateUnitOfWork();
        var june = Withheld(1000m);
        db.Context.PurchaseInvoices.Add(Purchase("JUN", new DateTime(2026, 6, 30), items: [june]));
        db.Context.PurchaseInvoices.Add(Return("DEV", new DateTime(2026, 7, 5), june, 400m));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Empty(data.Rows);
        Assert.Equal(0m, data.Net.Total);
    }

    [Theory]
    [InlineData("ItemCode")]
    [InlineData("CardCode")]
    [InlineData("BranchCode")]
    [InlineData("ContractCode")]
    public async Task Build_EachFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "CC-1", CardCode = "F001", ItemCode = "10001", UnitOfMeasureCode = "TN", HarvestSeasonCode = "2526", DeliveryLocationCode = "LOC1",
            Status = ContractStatus.Approved, CreationDate = Jul01,
        };
        db.Context.PurchaseContracts.Add(contract);
        var match = Withheld(1000m);
        match.PurchaseContractKey = contract.Key;
        var other = Withheld(1000m, itemCode: "99999");
        db.Context.PurchaseInvoices.Add(Purchase("MATCH", items: [match]));
        db.Context.PurchaseInvoices.Add(Purchase("OTHER", branchCode: "02", cardCode: "F002", items: [other]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "ItemCode": request.ItemCode = "10001"; break;
            case "CardCode": request.CardCode = "F001"; break;
            case "BranchCode": request.BranchCode = "01"; break;
            case "ContractCode": request.ContractCode = "CC-1"; break;
        }

        var data = await Service(db).BuildAsync(request);

        Assert.Equal(new[] { "MATCH" }, data.Rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task Execute_PassesTotalsToTheTemplate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [Withheld(1000m)]));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new FunruralWithholdingReportService(db, recorder, TestConfiguration.Erp("STANDALONE")).ExecuteAsync(Request());

        Assert.Equal("FunruralWithholding.frx", recorder.LastReportName);
        Assert.Equal(15m, recorder.LastParameters!["pNetTotal"]);
        Assert.Equal(1000m, recorder.LastParameters!["pGrossBase"]);
        Assert.Equal(1, recorder.LastParameters!["pItemCount"]);
    }

    /// <summary>Linha com Funrural de produtor PF fictício: 1,2% + 0,1% + 0,2% sobre a base.</summary>
    private static PurchaseInvoiceItem Withheld(decimal @base, string itemCode = "10001")
    {
        var item = PurchaseItem(itemCode: itemCode);
        SetFunrural(item, @base, 1.2m, 0.1m, 0.2m);
        return item;
    }

    private static void SetFunrural(PurchaseInvoiceItem item, decimal @base, decimal inss, decimal rat, decimal senar)
    {
        item.FunruralBase = @base;
        item.FunruralInssRate = inss;
        item.FunruralRatRate = rat;
        item.FunruralSenarRate = senar;
        item.FunruralInssValue = Math.Round(@base * inss / 100m, 2);
        item.FunruralRatValue = Math.Round(@base * rat / 100m, 2);
        item.FunruralSenarValue = Math.Round(@base * senar / 100m, 2);
    }

    /// <summary>Devolução de compra própria, como a cria o PurchaseInvoicesNfeReturnCreateService.</summary>
    private static PurchaseInvoice Return(string number, DateTime date, PurchaseInvoiceItem origin, decimal @base)
    {
        var line = PurchaseItem();
        line.PurchaseInvoiceItemOriginKey = origin.Key;
        SetFunrural(line, @base, origin.FunruralInssRate, origin.FunruralRatRate, origin.FunruralSenarRate);

        var invoice = Purchase(number, date, type: PurchaseInvoiceType.Return, issuer: DocumentIssuerType.Own, items: [line]);
        invoice.IsNfeReturn = true;
        invoice.PurchaseInvoiceOriginKey = origin.PurchaseInvoiceKey;
        invoice.NfeStatus = NfeStatus.Authorized;
        return invoice;
    }

    private static FunruralWithholdingReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static FunruralWithholdingRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
