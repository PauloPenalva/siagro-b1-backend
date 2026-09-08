using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAdvancesCreateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAdvancesCreateService Service() => new(
        _db,
        new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }));

    private async Task<PurchaseContract> SeedPurchaseContractAsync(
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            BranchCode = "01",
            UnitOfMeasureCode = "SC",
            HarvestSeasonCode = "2025",
            DeliveryLocationCode = "01",
            TotalVolume = 1000m,
            StandardPrice = 100m,
            StandardCurrency = CurrencyType.Brl,
            Type = ContractType.Fixed,
            Status = status
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private async Task<SalesContract> SeedSalesContractAsync(
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            BranchCode = "01",
            UnitOfMeasureCode = "SC",
            HarvestSeasonCode = "2025",
            TotalVolume = 1000m,
            StandardCurrency = CurrencyType.Brl,
            Type = ContractType.Fixed,
            Status = status
        };

        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task An_advance_on_a_purchase_contract_is_payable_and_not_blocked()
    {
        var contract = await SeedPurchaseContractAsync();

        var advance = await Service().ExecuteAsync(
            "Purchase", contract.Key, 5000m, DateTime.Today.AddDays(10), null, "tester");

        Assert.Equal(FinancialDirection.Payable, advance.Direction);
        Assert.Equal(FinancialDocumentNature.Advance, advance.Nature);
        Assert.Equal(FinancialDocumentOrigin.Manual, advance.OriginType);
        Assert.False(advance.IsBlockedForSettlement);
        Assert.Equal(5000m, advance.NetAmount);
        Assert.Equal("PRODUTOR TESTE", advance.CardName);
        Assert.Equal(contract.Key, advance.PurchaseContractKey);
    }

    [Fact]
    public async Task Two_advances_on_the_same_contract_are_both_accepted()
    {
        var contract = await SeedPurchaseContractAsync();
        var service = Service();

        await service.ExecuteAsync("Purchase", contract.Key, 1000m, DateTime.Today, null, "tester");
        await service.ExecuteAsync("Purchase", contract.Key, 2000m, DateTime.Today, null, "tester");

        Assert.Equal(2, _db.Context.FinancialDocuments.Count());
    }

    [Fact]
    public async Task Refuses_an_advance_on_a_contract_that_is_not_approved()
    {
        var contract = await SeedPurchaseContractAsync(ContractStatus.Draft);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("Purchase", contract.Key, 1000m, DateTime.Today, null, "tester"));

        Assert.Contains("aprovado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_non_positive_amount()
    {
        var contract = await SeedPurchaseContractAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("Purchase", contract.Key, 0m, DateTime.Today, null, "tester"));

        Assert.Contains("valor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_advance_on_a_sales_contract_is_receivable()
    {
        var contract = await SeedSalesContractAsync();

        var advance = await Service().ExecuteAsync(
            "Sales", contract.Key, 5000m, DateTime.Today.AddDays(10), null, "tester");

        Assert.Equal(FinancialDirection.Receivable, advance.Direction);
        Assert.Equal(FinancialDocumentNature.Advance, advance.Nature);
        Assert.Equal(contract.Key, advance.SalesContractKey);
        Assert.Null(advance.PurchaseContractKey);
    }

    [Fact]
    public async Task Refuses_an_unknown_contract_type()
    {
        var contract = await SeedPurchaseContractAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("Barter", contract.Key, 1000m, DateTime.Today, null, "tester"));

        Assert.Contains("Purchase", error.Message, StringComparison.Ordinal);
        Assert.Contains("Sales", error.Message, StringComparison.Ordinal);
    }
}
