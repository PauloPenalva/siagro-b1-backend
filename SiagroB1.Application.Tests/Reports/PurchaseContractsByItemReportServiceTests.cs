using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Regras do relatório de contratos de compra por produto e período: quais contratos
/// entram, em que ordem, e como cada célula de texto é montada.
/// </summary>
public class PurchaseContractsByItemReportServiceTests
{
    private static readonly DateTime Jul01 = new(2026, 7, 1);
    private static readonly DateTime Jul31 = new(2026, 7, 31);

    [Fact]
    public async Task BuildRows_IncludesContractCreatedLateOnTheLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-1", creationDate: new DateTime(2026, 7, 31, 23, 45, 0)));
        db.Context.PurchaseContracts.Add(NewContract("CC-2", creationDate: new DateTime(2026, 8, 1, 0, 5, 0)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CC-1" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_ExcludesCanceledButKeepsDraft()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-DRAFT", status: ContractStatus.Draft));
        db.Context.PurchaseContracts.Add(NewContract("CC-CANC", status: ContractStatus.Canceled));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CC-DRAFT" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Theory]
    [InlineData("ItemCode")]
    [InlineData("HarvestSeasonCode")]
    [InlineData("BranchCode")]
    [InlineData("DeliveryLocationCode")]
    [InlineData("CardCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-MATCH"));
        db.Context.PurchaseContracts.Add(NewContract(
            "CC-OTHER",
            itemCode: "OUTRO",
            harvestSeasonCode: "OUTRO",
            branchCode: "99",
            deliveryLocationCode: "OUTRO",
            cardCode: "OUTRO"));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var request = Request();
        switch (filter)
        {
            case "ItemCode": request.ItemCode = "10001"; break;
            case "HarvestSeasonCode": request.HarvestSeasonCode = "2026"; break;
            case "BranchCode": request.BranchCode = "01"; break;
            case "DeliveryLocationCode": request.DeliveryLocationCode = "AZ01"; break;
            case "CardCode": request.CardCode = "F001"; break;
        }

        var rows = await CreateService(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CC-MATCH" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_DeliveryPeriodMatchesOverlappingWindows()
    {
        var db = TestDb.CreateUnitOfWork();
        // Começa antes e termina dentro da janela: entra.
        db.Context.PurchaseContracts.Add(NewContract(
            "CC-OVERLAP",
            deliveryStart: new DateTime(2026, 7, 20),
            deliveryEnd: new DateTime(2026, 8, 10)));
        // Inteiramente depois da janela: fica de fora.
        db.Context.PurchaseContracts.Add(NewContract(
            "CC-AFTER",
            deliveryStart: new DateTime(2026, 9, 1),
            deliveryEnd: new DateTime(2026, 9, 30)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var request = Request();
        request.DeliveryFromDate = new DateTime(2026, 8, 1);
        request.DeliveryToDate = new DateTime(2026, 8, 31);

        var rows = await CreateService(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CC-OVERLAP" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_ConcatenatesBrokersIntoASingleRow()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = NewContract("CC-1");
        contract.Brokers =
        [
            new PurchaseContractBroker { CardCode = "B1", CardName = "João Silva", Commission = 2m, ComissionUmCode = "TN" },
            new PurchaseContractBroker { CardCode = "B2", CardName = "Maria Souza", Commission = 1.5m, ComissionUmCode = "TN" },
        ];
        db.Context.PurchaseContracts.Add(contract);
        db.Context.PurchaseContracts.Add(NewContract("CC-2"));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(
            "João Silva - 2,00 TN; Maria Souza - 1,50 TN",
            rows.Single(r => r.ContractCode == "CC-1").Commission);
        Assert.Equal("", rows.Single(r => r.ContractCode == "CC-2").Commission);
    }

    [Theory]
    [InlineData(FreightTerms.Cif, 45, "CIF - 45,00")]
    [InlineData(FreightTerms.Fob, 45, "FOB - 45,00")]
    [InlineData(FreightTerms.None, 45, "Sem frete")]
    public async Task BuildRows_FormatsFreight(FreightTerms terms, decimal cost, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = NewContract("CC-1");
        contract.FreightTerms = terms;
        contract.FreightCostStandard = cost;
        db.Context.PurchaseContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(expected, rows[0].Freight);
    }

    [Fact]
    public async Task BuildRows_OrdersByProductThenCreationDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-SOJA-2", itemCode: "10001", itemName: "SOJA", creationDate: new DateTime(2026, 7, 20)));
        db.Context.PurchaseContracts.Add(NewContract("CC-MILHO", itemCode: "10002", itemName: "MILHO", creationDate: new DateTime(2026, 7, 5)));
        db.Context.PurchaseContracts.Add(NewContract("CC-SOJA-1", itemCode: "10001", itemName: "SOJA", creationDate: new DateTime(2026, 7, 10)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CC-MILHO", "CC-SOJA-1", "CC-SOJA-2" }, rows.Select(r => r.ContractCode).ToArray());
        Assert.Equal("MILHO (10002)", rows[0].Product);
    }

    [Fact]
    public async Task BuildRows_FormatsRemainingColumns()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var contract = NewContract("CC-1");
        contract.StandardCashFlowDate = new DateTime(2026, 8, 15);
        contract.FunruralType = FunruralType.Bruto;
        contract.Status = ContractStatus.Approved;
        db.Context.PurchaseContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var row = (await CreateService(db).BuildRowsAsync(Request()))[0];

        // Filial e local de entrega saem só com o nome - o código não é impresso.
        Assert.Equal("MATRIZ", row.Branch);
        Assert.Equal("SILO 1", row.DeliveryLocation);
        Assert.Equal("AGRO SANTA FE", row.Supplier);
        Assert.Equal("Carlos Dias", row.Buyer);
        Assert.Equal("Bruto", row.Funrural);
        Assert.Equal("15/08/2026", row.PaymentForecast);
        Assert.Equal(1500m, row.Quantity);
        Assert.Equal("TN", row.UnitOfMeasure);
        Assert.Equal(128.5m, row.Price);
    }

    [Fact]
    public async Task BuildRows_WithoutName_FallsBackToTheCode()
    {
        var db = TestDb.CreateUnitOfWork();
        // Sem linha em BRANCHS e sem DeliveryLocationName: resta o código.
        var contract = NewContract("CC-1");
        contract.DeliveryLocationName = null;
        db.Context.PurchaseContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var row = (await CreateService(db).BuildRowsAsync(Request()))[0];

        Assert.Equal("01", row.Branch);
        Assert.Equal("AZ01", row.DeliveryLocation);
    }

    [Fact]
    public void BuildFiltersDescription_OmitsEmptyFiltersAndUsesRowDescriptions()
    {
        var request = Request();
        request.BranchCode = "01";
        request.DeliveryFromDate = new DateTime(2026, 8, 1);
        request.DeliveryToDate = new DateTime(2026, 9, 30);
        var rows = new List<PurchaseContractsByItemRowDto> { new() { Branch = "MATRIZ" } };

        var text = PurchaseContractsByItemReportService.BuildFiltersDescription(request, rows);

        Assert.Equal(
            "Emissão: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Entrega: 01/08/2026 a 30/09/2026",
            text);
    }

    [Fact]
    public void BuildFiltersDescription_WithoutRows_FallsBackToTheRawCode()
    {
        var request = Request();
        request.ItemCode = "10001";

        var text = PurchaseContractsByItemReportService.BuildFiltersDescription(request, []);

        Assert.Equal("Emissão: 01/07/2026 a 31/07/2026 | Produto: 10001", text);
    }

    private static PurchaseContractsByItemReportService CreateService(IUnitOfWork db) =>
        new(db, new StubFastReportService());

    private static PurchaseContractsByItemRequest Request() =>
        new() { FromDate = Jul01, ToDate = Jul31 };

    private static PurchaseContract NewContract(
        string code,
        DateTime? creationDate = null,
        ContractStatus status = ContractStatus.Approved,
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string harvestSeasonCode = "2026",
        string branchCode = "01",
        string deliveryLocationCode = "AZ01",
        string cardCode = "F001",
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
        DeliveryLocationCode = deliveryLocationCode,
        DeliveryLocationName = "SILO 1",
        CardCode = cardCode,
        CardName = "AGRO SANTA FE",
        AgentName = "Carlos Dias",
        UnitOfMeasureCode = "TN",
        TotalVolume = 1500m,
        StandardPrice = 128.5m,
        DeliveryStartDate = deliveryStart ?? new DateTime(2026, 8, 1),
        DeliveryEndDate = deliveryEnd ?? new DateTime(2026, 8, 31),
    };

    /// <summary>O teste de linhas não gera PDF; o serviço só precisa de uma dependência válida.</summary>
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
