using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ShipmentLoadsByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", new DateTime(2026, 7, 31, 23, 45, 0)));
        db.Context.ShipmentLoads.Add(Load("CG000002", new DateTime(2026, 8, 1)));
        db.Context.ShipmentLoads.Add(Load("CG000003", new DateTime(2026, 6, 30)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", status: ShipmentLoadStatus.Open));
        db.Context.ShipmentLoads.Add(Load("CG000002", status: ShipmentLoadStatus.Planned, total: 0m));
        db.Context.ShipmentLoads.Add(Load("CG000003", status: ShipmentLoadStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000001", "CG000002" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_CancelledLoadHasNoBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000001", status: ShipmentLoadStatus.Cancelled);
        load.InvoicedQuantity = 10_000m;
        db.Context.ShipmentLoads.Add(load);
        await Save(db);

        var request = Request();
        request.Statuses = [ShipmentLoadStatus.Cancelled];
        var row = (await Service(db).BuildRowsAsync(request)).Single();

        Assert.Equal("Cancelada", row.Status);
        Assert.Equal(0m, row.BalanceQuantity);
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("LoadType")]
    [InlineData("WarehouseCode")]
    [InlineData("CarrierCardCode")]
    [InlineData("TruckCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001"));
        db.Context.ShipmentLoads.Add(Load("CG000002", type: ShipmentLoadType.Removal, itemCode: "20001", itemName: "MILHO",
            branchCode: "99", truckCode: "XYZ9Z99", warehouseCode: "ARM99", carrierCardCode: "T999"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "LoadType": request.LoadType = ShipmentLoadType.Normal; break;
            case "WarehouseCode": request.WarehouseCode = "ARM01"; break;
            case "CarrierCardCode": request.CarrierCardCode = "T001"; break;
            case "TruckCode": request.TruckCode = "ABC1D23"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CG000001" }, rows.Select(r => r.Code));
    }

    // Review Focus 1: várias notas, cliente repetido, nota cancelada, status nulo.
    [Fact]
    public async Task BuildRows_CustomersComeFromTheLiveInvoicesOfTheLoad()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000102", "C002", "AGRO NORTE"));
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000103", "C001", "COOPERATIVA CENTRAL", status: null));
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000104", "C003", "CLIENTE CANCELADO", status: InvoiceStatus.Cancelled));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("AGRO NORTE, COOPERATIVA CENTRAL", row.Customers);
    }

    // Review Focus 2: carga sem nota.
    [Fact]
    public async Task BuildRows_LoadWithoutInvoiceShowsThePlannedCustomer()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", status: ShipmentLoadStatus.Planned, total: 0m,
            plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA"));
        db.Context.ShipmentLoads.Add(Load("CG000002", status: ShipmentLoadStatus.Planned, total: 0m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "FAZENDA BOA VISTA (planejado)", "" }, rows.Select(r => r.Customers));
    }

    // Review Focus 2: o filtro de Cliente segue a mesma regra (nota viva primeiro, planejado só sem nota).
    [Fact]
    public async Task BuildRows_CustomerFilterUsesTheInvoicesThenThePlan()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoiced = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        var planned = Load("CG000002", plannedCardCode: "C001", plannedCardName: "COOPERATIVA CENTRAL");
        var other = Load("CG000003");
        db.Context.ShipmentLoads.AddRange(invoiced, planned, other);
        db.Context.SalesInvoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(other, "000000102", "C002", "AGRO NORTE"));
        await Save(db);

        var request = Request();
        request.CardCode = "C001";
        Assert.Equal(new[] { "CG000001", "CG000002" }, (await Service(db).BuildRowsAsync(request)).Select(r => r.Code));

        request.CardCode = "C009";
        Assert.Empty(await Service(db).BuildRowsAsync(request));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsAndUsesTheDomainBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051", status: ShipmentLoadStatus.PartiallyInvoiced, total: 30_000m);
        load.InvoicedQuantity = 20_000m;
        load.ReturnedToWarehouseQuantity = 1_000m;
        load.TransshippedQuantity = 2_000m;
        load.DischargedQuantity = 19_950m;
        load.FreightPrice = 4_500.50m;
        db.Context.ShipmentLoads.Add(load);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("CG000051", row.Code);
        Assert.Equal("15/07/2026", row.LoadDate);
        Assert.Equal("Normal", row.Type);
        Assert.Equal("Faturada Parcial", row.Status);
        Assert.Equal("ABC1D23", row.Truck);
        Assert.Equal("TRANSPORTES RÁPIDO", row.Carrier);
        Assert.Equal("ARMAZÉM CENTRAL", row.Warehouse);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(30_000m, row.TotalQuantity);
        Assert.Equal(20_000m, row.InvoicedQuantity);
        Assert.Equal(1_000m, row.ReturnedQuantity);
        Assert.Equal(2_000m, row.TransshippedQuantity);
        Assert.Equal(19_950m, row.DischargedQuantity);
        Assert.Equal(7_000m, row.BalanceQuantity);
        Assert.Equal(4_500.50m, row.FreightValue);
    }

    [Fact]
    public async Task BuildRows_WithoutFreightPrintsZeroAndCarrierFallsBackToTheCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000001");
        load.FreightPrice = null;
        load.CarrierName = null;
        db.Context.ShipmentLoads.Add(load);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal(0m, row.FreightValue);
        Assert.Equal("T001", row.Carrier);
    }

    // Review Focus 5: UM mista não se mistura.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", new DateTime(2026, 7, 10)));
        db.Context.ShipmentLoads.Add(Load("CG000002", new DateTime(2026, 7, 5), uom: "TN", total: 30m));
        db.Context.ShipmentLoads.Add(Load("CG000003", new DateTime(2026, 7, 20), itemCode: "20001", itemName: "MILHO"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "CG000003", "CG000001", "CG000002" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenCodeInsideTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000002", new DateTime(2026, 7, 10)));
        db.Context.ShipmentLoads.Add(Load("CG000003", new DateTime(2026, 7, 5)));
        db.Context.ShipmentLoads.Add(Load("CG000001", new DateTime(2026, 7, 10)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000003", "CG000001", "CG000002" }, rows.Select(r => r.Code));
    }

    // Review Focus 5: total geral de quantidade só com uma UM.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001"));
        if (withTonnes)
            db.Context.ShipmentLoads.Add(Load("CG000002", uom: "TN", total: 30m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ShipmentLoadsByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("ShipmentLoadsByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
        Assert.Equal(withTonnes ? 2 : 1, ((ICollection<ShipmentLoadRowDto>)recorder.LastData!).Count);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var load = Load("CG000001", type: ShipmentLoadType.Removal);
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.LoadType = ShipmentLoadType.Removal;
        request.WarehouseCode = "ARM01";
        request.CarrierCardCode = "T001";
        request.TruckCode = "ABC1D23";
        request.CardCode = "C001";
        await new ShipmentLoadsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelada | Tipo: Remoção | Armazém: ARMAZÉM CENTRAL | " +
            "Transportadora: TRANSPORTES RÁPIDO | Placa: ABC1D23 | Cliente: COOPERATIVA CENTRAL",
            recorder.LastParameters!["pFilters"]);
    }

    [Fact]
    public async Task Execute_WithoutResultFallsBackToCodes()
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.ItemCode = "10001";
        request.WarehouseCode = "ARM01";
        request.CardCode = "C001";
        request.Statuses = [ShipmentLoadStatus.Invoiced, ShipmentLoadStatus.Open];
        await new ShipmentLoadsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Produto: 10001 | Situação: Carregada, Faturada | " +
            "Armazém: ARM01 | Cliente: C001",
            recorder.LastParameters!["pFilters"]);
        Assert.True((bool)recorder.LastParameters[FastReportService.SingleUomParameter]);
    }

    private static ShipmentLoadsByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static ShipmentLoadsByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
