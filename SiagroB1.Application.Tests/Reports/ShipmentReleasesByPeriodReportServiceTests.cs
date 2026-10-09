using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ShipmentReleasesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "31/07/2026" }, rows.Select(r => r.ReleaseDate));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, status: ReleaseStatus.Pending, deliveryLocationCode: "ARM01"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, status: ReleaseStatus.Completed, deliveryLocationCode: "ARM02"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, status: ReleaseStatus.Cancelled, deliveryLocationCode: "ARM03"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Pendente", "Finalizado" }, rows.Select(r => r.Status));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("ContractCode")]
    [InlineData("CardCode")]
    [InlineData("DeliveryLocationCode")]
    [InlineData("Origin")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var match = PurchaseContract("PC000001");
        var other = PurchaseContract("PC000002", cardCode: "F999", itemCode: "20001", itemName: "MILHO");
        db.Context.PurchaseContracts.AddRange(match, other);
        db.Context.ShipmentReleases.Add(PurchaseRelease(match));
        db.Context.ShipmentReleases.Add(PurchaseRelease(other, origin: ReleaseOrigin.Transshipment,
            deliveryLocationCode: "ARM99", branchCode: "99"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "ContractCode": request.ContractCode = "PC000001"; break;
            case "CardCode": request.CardCode = "F001"; break;
            case "DeliveryLocationCode": request.DeliveryLocationCode = "ARM01"; break;
            case "Origin": request.Origin = ReleaseOrigin.Standard; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "PC000001" }, rows.Select(r => r.Contract));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsFromTheContractAndTheRelease()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 250m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("15/07/2026", row.ReleaseDate);
        Assert.Equal("PC000001", row.Contract);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Supplier);
        Assert.Equal("ARMAZÉM CENTRAL", row.DeliveryLocation);
        Assert.Equal("Compra", row.Origin);
        Assert.Equal("Ativo", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(1_000m, row.ReleasedQuantity);
        Assert.Equal(250m, row.WithdrawnQuantity);
        Assert.Equal(750m, row.BalanceQuantity);
    }

    [Fact]
    public async Task BuildRows_ShowsTheOriginAndFallsBackToTheWarehouseCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, origin: ReleaseOrigin.SalesReturn,
            deliveryLocationCode: "ARM01"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, origin: ReleaseOrigin.Transshipment,
            deliveryLocationCode: "ARM02", deliveryLocationName: null));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Devolução", "Transbordo" }, rows.Select(r => r.Origin));
        Assert.Equal(new[] { "ARMAZÉM CENTRAL", "ARM02" }, rows.Select(r => r.DeliveryLocation));
    }

    // Review Focus 3: saldo pela regra do domínio (cancelada = 0; sem clamp de negativo).
    [Fact]
    public async Task BuildRows_BalanceFollowsTheDomainRule()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 400m,
            status: ReleaseStatus.Cancelled, deliveryLocationCode: "ARM01"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 0m,
            status: ReleaseStatus.Actived, deliveryLocationCode: "ARM02"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 1_200m,
            status: ReleaseStatus.Completed, deliveryLocationCode: "ARM03"));
        await Save(db);

        var request = Request();
        request.Statuses = [ReleaseStatus.Cancelled, ReleaseStatus.Actived, ReleaseStatus.Completed];
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { 400m, 0m, 1_200m }, rows.Select(r => r.WithdrawnQuantity));
        Assert.Equal(new[] { 0m, 1_000m, -200m }, rows.Select(r => r.BalanceQuantity));
    }

    // Review Focus 5.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        var sojaKg = PurchaseContract("PC000001");
        var sojaTn = PurchaseContract("PC000002", uom: "TN");
        var milho = PurchaseContract("PC000003", itemCode: "20001", itemName: "MILHO");
        db.Context.PurchaseContracts.AddRange(sojaKg, sojaTn, milho);
        db.Context.ShipmentReleases.Add(PurchaseRelease(sojaKg));
        db.Context.ShipmentReleases.Add(PurchaseRelease(sojaTn));
        db.Context.ShipmentReleases.Add(PurchaseRelease(milho));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "PC000003", "PC000001", "PC000002" }, rows.Select(r => r.Contract));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        var kg = PurchaseContract("PC000001");
        db.Context.PurchaseContracts.Add(kg);
        db.Context.ShipmentReleases.Add(PurchaseRelease(kg));
        if (withTonnes)
        {
            var tn = PurchaseContract("PC000002", uom: "TN");
            db.Context.PurchaseContracts.Add(tn);
            db.Context.ShipmentReleases.Add(PurchaseRelease(tn));
        }
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("ShipmentReleasesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.ContractCode = "PC000001";
        request.CardCode = "F001";
        request.DeliveryLocationCode = "ARM01";
        request.Origin = ReleaseOrigin.Standard;
        await new ShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da liberação: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelado | Contrato: PC000001 | Fornecedor: (F001) PRODUTOR RURAL | " +
            "Armazém de retirada: ARMAZÉM CENTRAL | Origem: Compra",
            recorder.LastParameters!["pFilters"]);
    }

    private static ShipmentReleasesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static ShipmentReleasesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
