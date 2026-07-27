using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Espelho de PurchaseContractsByItemReportServiceTests para o contrato de VENDA.
/// Cobre o que muda: Cliente/Vendedor no lugar de Fornecedor/Comprador, Região
/// Logística no lugar do local de entrega, Tipo Mercado, e frete só com o tipo.
/// </summary>
public class SalesContractsByItemReportServiceTests
{
    private static readonly DateTime Jul01 = new(2026, 7, 1);
    private static readonly DateTime Jul31 = new(2026, 7, 31);

    [Fact]
    public async Task BuildRows_IncludesContractCreatedLateOnTheLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(NewContract("CV-1", creationDate: new DateTime(2026, 7, 31, 23, 45, 0)));
        db.Context.SalesContracts.Add(NewContract("CV-2", creationDate: new DateTime(2026, 8, 1, 0, 5, 0)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CV-1" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_ExcludesCanceledButKeepsDraft()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(NewContract("CV-DRAFT", status: ContractStatus.Draft));
        db.Context.SalesContracts.Add(NewContract("CV-CANC", status: ContractStatus.Canceled));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CV-DRAFT" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Theory]
    [InlineData("ItemCode")]
    [InlineData("HarvestSeasonCode")]
    [InlineData("BranchCode")]
    [InlineData("LogisticRegionCode")]
    [InlineData("CardCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(NewContract("CV-MATCH"));
        db.Context.SalesContracts.Add(NewContract(
            "CV-OTHER",
            itemCode: "OUTRO",
            harvestSeasonCode: "OUTRO",
            branchCode: "99",
            logisticRegionCode: "OUTRO",
            cardCode: "OUTRO"));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var request = Request();
        switch (filter)
        {
            case "ItemCode": request.ItemCode = "10001"; break;
            case "HarvestSeasonCode": request.HarvestSeasonCode = "2026"; break;
            case "BranchCode": request.BranchCode = "01"; break;
            case "LogisticRegionCode": request.LogisticRegionCode = "RL01"; break;
            case "CardCode": request.CardCode = "C001"; break;
        }

        var rows = await CreateService(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CV-MATCH" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_DeliveryPeriodMatchesOverlappingWindows()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(NewContract(
            "CV-OVERLAP",
            deliveryStart: new DateTime(2026, 7, 20),
            deliveryEnd: new DateTime(2026, 8, 10)));
        db.Context.SalesContracts.Add(NewContract(
            "CV-AFTER",
            deliveryStart: new DateTime(2026, 9, 1),
            deliveryEnd: new DateTime(2026, 9, 30)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var request = Request();
        request.DeliveryFromDate = new DateTime(2026, 8, 1);
        request.DeliveryToDate = new DateTime(2026, 8, 31);

        var rows = await CreateService(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CV-OVERLAP" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Theory]
    [InlineData(FreightTerms.Cif, "CIF")]
    [InlineData(FreightTerms.Fob, "FOB")]
    [InlineData(FreightTerms.None, "Sem frete")]
    public async Task BuildRows_FreightPrintsOnlyTheTerms(FreightTerms terms, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = NewContract("CV-1");
        contract.FreightTerms = terms;
        db.Context.SalesContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(expected, rows[0].Freight);
    }

    [Theory]
    [InlineData(MarketType.Internal, "Interno")]
    [InlineData(MarketType.External, "Exportação")]
    public async Task BuildRows_TranslatesMarketType(MarketType market, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = NewContract("CV-1");
        contract.MarketType = market;
        db.Context.SalesContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(expected, rows[0].Market);
    }

    [Fact]
    public async Task BuildRows_OrdersByProductThenCreationDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(NewContract("CV-SOJA-2", itemCode: "10001", itemName: "SOJA", creationDate: new DateTime(2026, 7, 20)));
        db.Context.SalesContracts.Add(NewContract("CV-MILHO", itemCode: "10002", itemName: "MILHO", creationDate: new DateTime(2026, 7, 5)));
        db.Context.SalesContracts.Add(NewContract("CV-SOJA-1", itemCode: "10001", itemName: "SOJA", creationDate: new DateTime(2026, 7, 10)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CV-MILHO", "CV-SOJA-1", "CV-SOJA-2" }, rows.Select(r => r.ContractCode).ToArray());
        Assert.Equal("MILHO (10002)", rows[0].Product);
    }

    [Fact]
    public async Task BuildRows_FormatsRemainingColumns()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.LogisticRegions.Add(new LogisticRegion { Code = "RL01", Name = "PORTO DE PARANAGUA" });
        var contract = NewContract("CV-1");
        contract.StandardCashFlowDate = new DateTime(2026, 8, 15);
        db.Context.SalesContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var row = (await CreateService(db).BuildRowsAsync(Request()))[0];

        // Filial e região logística saem só com o nome - o código não é impresso.
        Assert.Equal("MATRIZ", row.Branch);
        Assert.Equal("PORTO DE PARANAGUA", row.LogisticRegion);
        Assert.Equal("COOPERATIVA CENTRAL", row.Customer);
        Assert.Equal("Carlos Dias", row.Seller);
        Assert.Equal("15/08/2026", row.PaymentForecast);
        Assert.Equal(1500m, row.Quantity);
        Assert.Equal("TN", row.UnitOfMeasure);
        Assert.Equal(128.5m, row.Price);
    }

    [Fact]
    public async Task BuildRows_WithoutName_FallsBackToTheCode()
    {
        var db = TestDb.CreateUnitOfWork();
        // Sem linha em BRANCHS nem em LOGISTIC_REGIONS: resta o código.
        db.Context.SalesContracts.Add(NewContract("CV-1"));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var row = (await CreateService(db).BuildRowsAsync(Request()))[0];

        Assert.Equal("01", row.Branch);
        Assert.Equal("RL01", row.LogisticRegion);
    }

    [Fact]
    public void BuildFiltersDescription_OmitsEmptyFiltersAndUsesRowDescriptions()
    {
        var request = Request();
        request.LogisticRegionCode = "RL01";
        request.DeliveryFromDate = new DateTime(2026, 8, 1);
        request.DeliveryToDate = new DateTime(2026, 9, 30);
        var rows = new List<SalesContractsByItemRowDto> { new() { LogisticRegion = "PORTO DE PARANAGUA" } };

        var text = SalesContractsByItemReportService.BuildFiltersDescription(request, rows);

        Assert.Equal(
            "Emissão: 01/07/2026 a 31/07/2026 | Região logística: PORTO DE PARANAGUA | Entrega: 01/08/2026 a 30/09/2026",
            text);
    }

    [Fact]
    public void BuildFiltersDescription_WithoutRows_FallsBackToTheRawCode()
    {
        var request = Request();
        request.CardCode = "C001";

        var text = SalesContractsByItemReportService.BuildFiltersDescription(request, []);

        Assert.Equal("Emissão: 01/07/2026 a 31/07/2026 | Cliente: C001", text);
    }

    private static SalesContractsByItemReportService CreateService(IUnitOfWork db) =>
        new(db, new StubFastReportService());

    private static SalesContractsByItemRequest Request() =>
        new() { FromDate = Jul01, ToDate = Jul31 };

    private static SalesContract NewContract(
        string code,
        DateTime? creationDate = null,
        ContractStatus status = ContractStatus.Approved,
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string harvestSeasonCode = "2026",
        string branchCode = "01",
        string logisticRegionCode = "RL01",
        string cardCode = "C001",
        DateTime? deliveryStart = null,
        DateTime? deliveryEnd = null) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CreationDate = creationDate ?? new DateTime(2026, 7, 15),
        Status = status,
        ItemCode = itemCode,
        ItemName = itemName,
        HarvestSeasonCode = harvestSeasonCode,
        BranchCode = branchCode,
        LogisticRegionCode = logisticRegionCode,
        CardCode = cardCode,
        CardName = "COOPERATIVA CENTRAL",
        AgentName = "Carlos Dias",
        UnitOfMeasureCode = "TN",
        TotalVolume = 1500m,
        Price = 128.5m,
        DeliveryStartDate = deliveryStart ?? new DateTime(2026, 8, 1),
        DeliveryEndDate = deliveryEnd ?? new DateTime(2026, 8, 31),
    };

    private sealed class StubFastReportService : IFastReportService
    {
        public Task<byte[]> GeneratePdfAsync(string reportName, Dictionary<string, object> parameters) =>
            Task.FromResult(Array.Empty<byte>());

        public Task<byte[]> GeneratePdfAsync<T>(
            string reportName,
            ICollection<T> data,
            string dataSourceName,
            string refName,
            Dictionary<string, object> parameters) => Task.FromResult(Array.Empty<byte>());
    }
}
