using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;
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
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }),
        new FinancialDocumentsSettleService(_db));

    private void SeedAccount(string code = "CX01", bool inactive = false, CurrencyType currency = CurrencyType.Brl)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = code, Name = "Caixa " + code, Inactive = inactive, Currency = currency
        });
        _db.Context.SaveChanges();
    }

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

        if (!_db.Context.FinancialAccounts.Any(x => x.Code == "CX01")) SeedAccount();
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

        if (!_db.Context.FinancialAccounts.Any(x => x.Code == "CX01")) SeedAccount();
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task An_advance_on_a_purchase_contract_is_payable_and_not_blocked()
    {
        var contract = await SeedPurchaseContractAsync();

        var advance = await Service().ExecuteAsync(
            "Purchase", contract.Key, 5000m, DateTime.Today.AddDays(10), "CX01", DateTime.Today, null, "tester");

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

        await service.ExecuteAsync("Purchase", contract.Key, 1000m, DateTime.Today, "CX01", DateTime.Today, null, "tester");
        await service.ExecuteAsync("Purchase", contract.Key, 2000m, DateTime.Today, "CX01", DateTime.Today, null, "tester");

        Assert.Equal(2, _db.Context.FinancialDocuments.Count());
    }

    [Fact]
    public async Task Refuses_an_advance_on_a_contract_that_is_not_approved()
    {
        var contract = await SeedPurchaseContractAsync(ContractStatus.Draft);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("Purchase", contract.Key, 1000m, DateTime.Today, "CX01", DateTime.Today, null, "tester"));

        Assert.Contains("aprovado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_non_positive_amount()
    {
        var contract = await SeedPurchaseContractAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("Purchase", contract.Key, 0m, DateTime.Today, "CX01", DateTime.Today, null, "tester"));

        Assert.Contains("valor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_advance_on_a_sales_contract_is_receivable()
    {
        var contract = await SeedSalesContractAsync();

        var advance = await Service().ExecuteAsync(
            "Sales", contract.Key, 5000m, DateTime.Today.AddDays(10), "CX01", DateTime.Today, null, "tester");

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
            () => Service().ExecuteAsync("Barter", contract.Key, 1000m, DateTime.Today, "CX01", DateTime.Today, null, "tester"));

        Assert.Contains("Purchase", error.Message, StringComparison.Ordinal);
        Assert.Contains("Sales", error.Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task The_advance_is_born_settled_in_the_chosen_account()
    {
        var contract = await SeedPurchaseContractAsync();
        var paidOn = DateTime.Today.AddDays(-2);

        var advance = await Service().ExecuteAsync(
            "Purchase", contract.Key, 5000m, DateTime.Today.AddDays(10), "CX01", paidOn, null, "tester");

        var settlement = Assert.Single(_db.Context.FinancialSettlements.Where(x => x.FinancialDocumentKey == advance.Key));
        Assert.Equal("CX01", settlement.FinancialAccountCode);
        Assert.Equal(5000m, settlement.Amount);
        Assert.Equal(paidOn, settlement.SettlementDate);

        var saved = _db.Context.FinancialDocuments.Single(x => x.Key == advance.Key);
        Assert.Equal(5000m, saved.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, saved.Status);
        Assert.Equal(5000m, saved.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Refuses_without_account_and_writes_nothing()
    {
        var contract = await SeedPurchaseContractAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            "Purchase", contract.Key, 5000m, DateTime.Today, " ", DateTime.Today, null, "tester"));

        Assert.Empty(_db.Context.FinancialDocuments);
        Assert.Empty(_db.Context.FinancialSettlements);
    }

    [Fact]
    public async Task Refuses_an_inactive_account_and_writes_nothing()
    {
        var contract = await SeedPurchaseContractAsync();
        SeedAccount("CX02", inactive: true);

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            "Purchase", contract.Key, 5000m, DateTime.Today, "CX02", DateTime.Today, null, "tester"));

        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Creates_advance_without_contract_paid_on_account()
    {
        SeedAccount("BCO1");

        var advance = await Service().ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "F0001", "01", 500m, DateTime.Today.AddDays(5), "BCO1", DateTime.Today,
            "prestador", "tester");

        Assert.Equal(FinancialDocumentNature.Advance, advance.Nature);
        Assert.Equal(FinancialDirection.Payable, advance.Direction);
        Assert.Null(advance.PurchaseContractKey);
        Assert.Null(advance.SalesContractKey);
        Assert.Equal(CurrencyType.Brl, advance.Currency);
        Assert.Equal("01", advance.BranchCode);
        Assert.Equal("PRODUTOR TESTE", advance.CardName);
        Assert.True(string.IsNullOrEmpty(advance.OriginDocNumber));
        var settlement = Assert.Single(_db.Context.FinancialSettlements.Where(x => x.FinancialDocumentKey == advance.Key));
        Assert.Equal("BCO1", settlement.FinancialAccountCode);
        Assert.Equal(500m, settlement.Amount);
    }

    [Fact]
    public async Task Without_contract_currency_comes_from_the_account()
    {
        SeedAccount("USD1", currency: CurrencyType.Usd);

        var advance = await Service().ExecuteWithoutContractAsync(
            FinancialDirection.Receivable, "F0001", "01", 500m, DateTime.Today, "USD1", DateTime.Today, null, "tester");

        Assert.Equal(CurrencyType.Usd, advance.Currency);
        Assert.Equal(FinancialDirection.Receivable, advance.Direction);
    }

    [Fact]
    public async Task Without_contract_requires_partner()
    {
        SeedAccount("BCO1");

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteWithoutContractAsync(
            FinancialDirection.Payable, " ", "01", 500m, DateTime.Today, "BCO1", DateTime.Today, null, "tester"));

        Assert.Contains("Informe o parceiro", error.Message);
        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Without_contract_requires_branch()
    {
        SeedAccount("BCO1");

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "F0001", "", 500m, DateTime.Today, "BCO1", DateTime.Today, null, "tester"));

        Assert.Contains("Informe a filial", error.Message);
    }

    [Fact]
    public async Task Without_contract_rejects_unknown_partner()
    {
        SeedAccount("BCO1");

        await Assert.ThrowsAsync<NotFoundException>(() => Service().ExecuteWithoutContractAsync(
            FinancialDirection.Payable, "X9999", "01", 500m, DateTime.Today, "BCO1", DateTime.Today, null, "tester"));

        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Refuses_an_unknown_account()
    {
        var contract = await SeedPurchaseContractAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            "Purchase", contract.Key, 5000m, DateTime.Today, "NOPE", DateTime.Today, null, "tester"));

        Assert.Empty(_db.Context.FinancialDocuments);
    }
}
