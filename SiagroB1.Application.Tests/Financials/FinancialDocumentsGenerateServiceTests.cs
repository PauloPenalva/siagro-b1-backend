using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsGenerateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateService Service() => new(
        _db.Context,
        new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }));

    private static PurchaseContract Contract(
        DateTime? cashFlowDate = null,
        decimal totalVolume = 1000m,
        decimal standardPrice = 100m) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC0001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        ItemName = "SOJA GRAO",
        BranchCode = "01",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
        StandardPrice = standardPrice,
        StandardCashFlowDate = cashFlowDate,
        StandardCurrency = CurrencyType.Brl,
        Type = ContractType.Fixed,
        Status = ContractStatus.Approved
    };

    private static PurchaseContractPriceFixation Fixation(
        PurchaseContract contract,
        DateTime? financialDueDate = null,
        decimal? volume = null,
        decimal? price = null) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        FixationVolume = volume ?? contract.TotalVolume,
        FixationPrice = price ?? contract.StandardPrice,
        FinancialDueDate = financialDueDate,
        Status = PriceFixationStatus.Confirmed
    };

    [Fact]
    public async Task A_confirmed_fixation_produces_one_payable_provisional_document()
    {
        var contract = Contract(cashFlowDate: new DateTime(2026, 12, 31));
        var fixation = Fixation(contract);

        await Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        var document = Assert.Single(_db.Context.FinancialDocuments);
        Assert.Equal(FinancialDirection.Payable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Provisional, document.Nature);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
        Assert.True(document.IsBlockedForSettlement);
        Assert.Equal(100_000m, document.NetAmount);
        Assert.Equal(new DateTime(2026, 12, 31), document.DueDate);
        Assert.Equal(fixation.Key, document.OriginKey);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractPriceFixation, document.OriginType);
        Assert.Equal("PC0001", document.OriginDocNumber);
        Assert.Equal("PRODUTOR TESTE", document.CardName);
        Assert.Equal(contract.Key, document.PurchaseContractKey);
    }

    [Fact]
    public async Task The_fixation_due_date_wins_over_the_contract_forecast()
    {
        var contract = Contract(cashFlowDate: new DateTime(2026, 12, 31));
        var fixation = Fixation(contract, financialDueDate: new DateTime(2026, 6, 15));

        await Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(new DateTime(2026, 6, 15), _db.Context.FinancialDocuments.Single().DueDate);
    }

    [Fact]
    public async Task The_amount_is_rounded_to_two_decimals()
    {
        var contract = Contract(cashFlowDate: DateTime.Today);
        var fixation = Fixation(contract, volume: 333.333m, price: 100.005m);

        await Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        // 333.333 * 100.005 = 33334.966665 -> 33334.97
        Assert.Equal(33_334.97m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Refuses_a_fixed_price_contract_without_the_payment_forecast()
    {
        var contract = Contract(cashFlowDate: null);
        var fixation = Fixation(contract);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester"));

        Assert.Contains("previsão de pagamento", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_to_be_determined_fixation_without_a_financial_due_date()
    {
        var contract = Contract(cashFlowDate: null);
        contract.Type = ContractType.ToBeDetermined;
        var fixation = Fixation(contract, financialDueDate: null);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester"));

        Assert.Contains("vencimento financeiro", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generating_twice_for_the_same_fixation_does_not_duplicate()
    {
        var contract = Contract(cashFlowDate: DateTime.Today);
        var fixation = Fixation(contract);
        var service = Service();

        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();
        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task A_canceled_document_frees_the_origin_for_regeneration()
    {
        var contract = Contract(cashFlowDate: DateTime.Today);
        var fixation = Fixation(contract);
        var service = Service();

        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        _db.Context.FinancialDocuments.Single().Status = FinancialDocumentStatus.Canceled;
        await _db.Context.SaveChangesAsync();

        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(2, _db.Context.FinancialDocuments.Count());
        Assert.Single(_db.Context.FinancialDocuments.Where(x => x.Status == FinancialDocumentStatus.Open));
    }
}
