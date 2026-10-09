using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesShipmentsByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_TakesOnlySalesShipments()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001"));
        var purchaseLeg = SalesShipment("RO000002");
        purchaseLeg.TransactionType = StorageTransactionType.Purchase;
        db.Context.StorageTransactions.Add(purchaseLeg);
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", status: StorageTransactionsStatus.Confirmed));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", status: StorageTransactionsStatus.Invoiced));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", status: StorageTransactionsStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000001", "RO000002" }, rows.Select(r => r.Code));
        Assert.Equal(new[] { "Confirmado", "Faturado" }, rows.Select(r => r.Status));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("WarehouseCode")]
    [InlineData("TruckCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", itemCode: "20001", itemName: "MILHO",
            branchCode: "99", warehouseCode: "ARM99", truckCode: "XYZ9Z99"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "WarehouseCode": request.WarehouseCode = "ARM01"; break;
            case "TruckCode": request.TruckCode = "ABC1D23"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "RO000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsAndSumsTheDiscounts()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load, gross: 30_000m,
            drying: 300m, cleaning: 150m, others: 50m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("15/07/2026", row.TransactionDate);
        Assert.Equal("RO000001", row.Code);
        Assert.Equal("CG000051", row.Load);
        Assert.Equal("ABC1D23", row.Truck);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Supplier);
        Assert.Equal("COOPERATIVA CENTRAL", row.Customers);
        Assert.Equal("ARMAZÉM CENTRAL", row.Warehouse);
        Assert.Equal("000123/1", row.InvoiceNumber);
        Assert.Equal("Confirmado", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(30_000m, row.GrossWeight);
        Assert.Equal(500m, row.DiscountWeight);
        Assert.Equal(29_500m, row.NetWeight);
    }

    [Fact]
    public async Task BuildRows_SupplierInvoiceNeverLeavesALooseSlash()
    {
        var db = TestDb.CreateUnitOfWork();
        var noSeries = SalesShipment("RO000001");
        noSeries.InvoiceSerie = null;
        var noNumber = SalesShipment("RO000002");
        noNumber.InvoiceNumber = null;
        db.Context.StorageTransactions.AddRange(noSeries, noNumber);
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "000123", "" }, rows.Select(r => r.InvoiceNumber));
    }

    // Romaneio sem carga não some do relatório e não inventa cliente.
    [Fact]
    public async Task BuildRows_ShipmentWithoutLoadHasNoLoadNorCustomer()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000051", "" }, rows.Select(r => r.Load));
        Assert.Equal(new[] { "COOPERATIVA CENTRAL", "" }, rows.Select(r => r.Customers));
    }

    // Filtro de vínculo com carga: com, sem ou ambos.
    [Theory]
    [InlineData(true, new[] { "RO000001" })]
    [InlineData(false, new[] { "RO000002" })]
    [InlineData(null, new[] { "RO000001", "RO000002" })]
    public async Task BuildRows_LoadLinkFilter(bool? hasLoad, string[] expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002"));
        await Save(db);

        var request = Request();
        request.HasLoad = hasLoad;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(expected, rows.Select(r => r.Code));
    }

    // O filtro de Cliente usa as notas da carga; sem carga, nunca casa.
    [Fact]
    public async Task BuildRows_CustomerFilterNeverMatchesAShipmentWithoutLoad()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoiced = Load("CG000051");
        var planned = Load("CG000052", plannedCardCode: "C001", plannedCardName: "COOPERATIVA CENTRAL");
        var other = Load("CG000053");
        db.Context.ShipmentLoads.AddRange(invoiced, planned, other);
        db.Context.SalesInvoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(other, "000000102", "C002", "AGRO NORTE"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: invoiced));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", load: planned));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", load: other));
        db.Context.StorageTransactions.Add(SalesShipment("RO000004"));
        await Save(db);

        var request = Request();
        request.CardCode = "C001";
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "RO000001", "RO000002" }, rows.Select(r => r.Code));
        Assert.Equal("(planejado) COOPERATIVA CENTRAL", rows[1].Customers);
    }

    // Agrupamento por produto + UM.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", new DateTime(2026, 7, 10)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", new DateTime(2026, 7, 5), uom: "TN", gross: 30m));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", new DateTime(2026, 7, 20), itemCode: "20001", itemName: "MILHO"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "RO000003", "RO000001", "RO000002" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenCodeInsideTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", new DateTime(2026, 7, 10)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", new DateTime(2026, 7, 5)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", new DateTime(2026, 7, 10)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000003", "RO000001", "RO000002" }, rows.Select(r => r.Code));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001"));
        if (withTonnes)
            db.Context.StorageTransactions.Add(SalesShipment("RO000002", uom: "TN", gross: 30m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new SalesShipmentsByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("SalesShipmentsByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.WarehouseCode = "ARM01";
        request.TruckCode = "ABC1D23";
        request.CardCode = "C001";
        request.HasLoad = true;
        await new SalesShipmentsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data do romaneio: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelado | Armazém: ARMAZÉM CENTRAL | Placa: ABC1D23 | " +
            "Cliente: COOPERATIVA CENTRAL | Vínculo com carga: Com carga",
            recorder.LastParameters!["pFilters"]);
    }

    [Fact]
    public async Task Execute_WithoutResultFallsBackToCodes()
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.WarehouseCode = "ARM01";
        request.CardCode = "C001";
        request.HasLoad = false;
        await new SalesShipmentsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data do romaneio: 01/07/2026 a 31/07/2026 | Situação: todas, exceto Cancelado | Armazém: ARM01 | " +
            "Cliente: C001 | Vínculo com carga: Sem carga",
            recorder.LastParameters!["pFilters"]);
    }

    private static SalesShipmentsByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static SalesShipmentsByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
