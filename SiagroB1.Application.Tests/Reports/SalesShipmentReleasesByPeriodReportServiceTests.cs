using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesShipmentReleasesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "31/07/2026" }, rows.Select(r => r.ReleaseDate));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, status: ReleaseStatus.Actived, deliveryLocationCode: "L01"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, status: ReleaseStatus.Paused, deliveryLocationCode: "L02"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, status: ReleaseStatus.Cancelled, deliveryLocationCode: "L03"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Ativo", "Pausado" }, rows.Select(r => r.Status));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("ContractCode")]
    [InlineData("CardCode")]
    [InlineData("AgentCode")]
    [InlineData("LogisticRegionCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var match = SalesContract("CV000001");
        var other = SalesContract("CV000002", cardCode: "C999", itemCode: "20001", itemName: "MILHO",
            agentCode: 99, regionCode: "R99");
        db.Context.SalesContracts.AddRange(match, other);
        db.Context.SalesShipmentReleases.Add(SalesRelease(match));
        db.Context.SalesShipmentReleases.Add(SalesRelease(other, branchCode: "99"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "ContractCode": request.ContractCode = "CV000001"; break;
            case "CardCode": request.CardCode = "C001"; break;
            case "AgentCode": request.AgentCode = 7; break;
            case "LogisticRegionCode": request.LogisticRegionCode = "R01"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CV000001" }, rows.Select(r => r.Contract));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsFromTheContract()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LogisticRegions.Add(Region());
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 400m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("15/07/2026", row.ReleaseDate);
        Assert.Equal("30/09/2026", row.DeliveryDeadline);
        Assert.Equal("CV000001", row.Contract);
        Assert.Equal("(C001) COOPERATIVA CENTRAL", row.Customer);
        Assert.Equal("JOÃO VENDEDOR", row.Agent);
        Assert.Equal("PORTO DE PARANAGUÁ", row.DeliveryLocation);
        Assert.Equal("NORTE", row.Region);
        Assert.Equal("Ativo", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(1_000m, row.ReleasedQuantity);
        Assert.Equal(400m, row.ConsumedQuantity);
        Assert.Equal(600m, row.BalanceQuantity);
    }

    [Fact]
    public async Task BuildRows_FallsBackToCodesWhenNamesAreMissing()
    {
        var db = TestDb.CreateUnitOfWork();
        var noAgentName = SalesContract("CV000001", agentName: null);
        var noAgent = SalesContract("CV000002", agentCode: null, agentName: null);
        db.Context.SalesContracts.AddRange(noAgentName, noAgent);
        db.Context.SalesShipmentReleases.Add(SalesRelease(noAgentName, deliveryLocationName: null));
        db.Context.SalesShipmentReleases.Add(SalesRelease(noAgent));
        await Save(db); // sem LogisticRegion "R01" na tabela: a região cai para o código

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "7", "" }, rows.Select(r => r.Agent));
        Assert.Equal(new[] { "L01", "PORTO DE PARANAGUÁ" }, rows.Select(r => r.DeliveryLocation));
        Assert.All(rows, r => Assert.Equal("R01", r.Region));
    }

    // Review Focus 3: saldo pela regra do domínio (cancelada = 0; sem clamp de negativo).
    [Fact]
    public async Task BuildRows_BalanceFollowsTheDomainRule()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 400m,
            status: ReleaseStatus.Cancelled, deliveryLocationCode: "L01"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 0m,
            status: ReleaseStatus.Actived, deliveryLocationCode: "L02"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 1_200m,
            status: ReleaseStatus.Completed, deliveryLocationCode: "L03"));
        await Save(db);

        var request = Request();
        request.Statuses = [ReleaseStatus.Cancelled, ReleaseStatus.Actived, ReleaseStatus.Completed];
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { 400m, 0m, 1_200m }, rows.Select(r => r.ConsumedQuantity));
        Assert.Equal(new[] { 0m, 1_000m, -200m }, rows.Select(r => r.BalanceQuantity));
    }

    // Review Focus 5.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        var sojaKg = SalesContract("CV000001");
        var sojaTn = SalesContract("CV000002", uom: "TN");
        var milho = SalesContract("CV000003", itemCode: "20001", itemName: "MILHO");
        db.Context.SalesContracts.AddRange(sojaKg, sojaTn, milho);
        db.Context.SalesShipmentReleases.Add(SalesRelease(sojaKg));
        db.Context.SalesShipmentReleases.Add(SalesRelease(sojaTn));
        db.Context.SalesShipmentReleases.Add(SalesRelease(milho));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "CV000003", "CV000001", "CV000002" }, rows.Select(r => r.Contract));
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenContractInsideTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        var a = SalesContract("CV000001");
        var b = SalesContract("CV000002");
        db.Context.SalesContracts.AddRange(a, b);
        db.Context.SalesShipmentReleases.Add(SalesRelease(b, new DateTime(2026, 7, 10)));
        db.Context.SalesShipmentReleases.Add(SalesRelease(a, new DateTime(2026, 7, 10)));
        db.Context.SalesShipmentReleases.Add(SalesRelease(b, new DateTime(2026, 7, 5)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "05/07/2026", "10/07/2026", "10/07/2026" }, rows.Select(r => r.ReleaseDate));
        Assert.Equal(new[] { "CV000002", "CV000001", "CV000002" }, rows.Select(r => r.Contract));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        var kg = SalesContract("CV000001");
        db.Context.SalesContracts.Add(kg);
        db.Context.SalesShipmentReleases.Add(SalesRelease(kg));
        if (withTonnes)
        {
            var tn = SalesContract("CV000002", uom: "TN");
            db.Context.SalesContracts.Add(tn);
            db.Context.SalesShipmentReleases.Add(SalesRelease(tn));
        }
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new SalesShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("SalesShipmentReleasesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.LogisticRegions.Add(Region());
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.ContractCode = "CV000001";
        request.CardCode = "C001";
        request.AgentCode = 7;
        request.LogisticRegionCode = "R01";
        await new SalesShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da liberação: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelado | Contrato: CV000001 | Cliente: (C001) COOPERATIVA CENTRAL | " +
            "Vendedor: JOÃO VENDEDOR | Região: NORTE",
            recorder.LastParameters!["pFilters"]);
    }

    [Fact]
    public async Task Execute_WithoutResultFallsBackToCodes()
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.CardCode = "C001";
        request.AgentCode = 7;
        request.LogisticRegionCode = "R01";
        request.Statuses = [ReleaseStatus.Cancelled];
        await new SalesShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da liberação: 01/07/2026 a 31/07/2026 | Situação: Cancelado | Cliente: C001 | Vendedor: 7 | Região: R01",
            recorder.LastParameters!["pFilters"]);
    }

    private static SalesShipmentReleasesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static SalesShipmentReleasesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
