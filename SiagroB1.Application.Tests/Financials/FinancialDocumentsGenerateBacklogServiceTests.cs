using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsGenerateBacklogServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateBacklogService Service() => new(
        _db,
        new FinancialDocumentsGenerateService(
            _db.Context,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(
                new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE", ["C0001"] = "CLIENTE TESTE" })));

    private async Task<PurchaseContract> SeedApprovedFixedContractAsync(DateTime? cashFlowDate)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            BranchCode = "01",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            CreationDate = DateTime.Today,
            TotalVolume = 1000m,
            StandardPrice = 100m,
            StandardCashFlowDate = cashFlowDate,
            StandardCurrency = CurrencyType.Brl,
            Type = ContractType.Fixed,
            Status = ContractStatus.Approved
        };

        contract.PriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = contract.TotalVolume,
            FixationPrice = contract.StandardPrice,
            Status = PriceFixationStatus.Confirmed
        });

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private async Task<SalesContract> SeedApprovedFixedSalesContractAsync(DateTime? cashFlowDate)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC0001",
            CardCode = "C0001",
            ItemCode = "SOJA",
            BranchCode = "01",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            CreationDate = DateTime.Today,
            TotalVolume = 1000m,
            Price = 120m,
            StandardCashFlowDate = cashFlowDate,
            StandardCurrency = CurrencyType.Brl,
            Type = ContractType.Fixed,
            Status = ContractStatus.Approved
        };

        contract.PriceFixations.Add(new SalesContractPriceFixation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            FixationVolume = contract.TotalVolume,
            FixationPrice = contract.Price,
            Status = PriceFixationStatus.Confirmed
        });

        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task A_dry_run_reports_what_would_be_created_and_writes_nothing()
    {
        await SeedApprovedFixedContractAsync(cashFlowDate: DateTime.Today);

        var result = await Service().ExecuteAsync(
            branchCode: null,
            fromDate: DateTime.Today.AddYears(-1),
            toDate: DateTime.Today.AddDays(1),
            dryRun: true,
            userName: "tester");

        Assert.True(result.DryRun);
        Assert.Equal(1, result.EligibleFixations);
        Assert.Equal(0, result.Generated);
        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Running_it_twice_generates_only_once()
    {
        await SeedApprovedFixedContractAsync(cashFlowDate: DateTime.Today);
        var service = Service();
        var from = DateTime.Today.AddYears(-1);
        var to = DateTime.Today.AddDays(1);

        await service.ExecuteAsync(null, from, to, dryRun: false, "tester");
        var second = await service.ExecuteAsync(null, from, to, dryRun: false, "tester");

        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.SkippedAlreadyGenerated);
        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task A_contract_without_a_due_date_is_reported_not_thrown()
    {
        await SeedApprovedFixedContractAsync(cashFlowDate: null);

        var result = await Service().ExecuteAsync(
            null, DateTime.Today.AddYears(-1), DateTime.Today.AddDays(1), dryRun: false, "tester");

        Assert.Equal(1, result.SkippedWithoutDueDate);
        Assert.Equal(0, result.Generated);
    }

    /// <summary>
    /// O brief original só varria PurchaseContracts. Nem o spec nem o DTO limitam este
    /// backlog a compra — deixar a venda de fora esconderia metade do portfólio, sem
    /// nenhum sinal até alguém procurar manualmente por um título de venda nunca gerado.
    /// Este teste prova que o laço espelho para SalesContract existe e é somado no MESMO
    /// contador: sem ele, a omissão passaria despercebida com todos os outros testes verdes.
    /// </summary>
    /// <summary>
    /// Preço fixo; fixação Confirmed cobrindo <paramref name="totalVolume"/> @ 100; um washout
    /// Approved de <paramref name="washedFixedVolume"/> fixado apontando para ela.
    /// </summary>
    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedApprovedFixedContractWithWashoutAsync(
        decimal washedFixedVolume, decimal totalVolume = 1_000m)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC0002",
            CardCode = "F0001",
            ItemCode = "SOJA",
            BranchCode = "01",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            CreationDate = DateTime.Today,
            TotalVolume = totalVolume,
            StandardPrice = 100m,
            StandardCashFlowDate = DateTime.Today,
            StandardCurrency = CurrencyType.Brl,
            Type = ContractType.Fixed,
            Status = ContractStatus.Approved
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = totalVolume,
            FixationPrice = contract.StandardPrice,
            FinancialDueDate = DateTime.Today,
            Status = PriceFixationStatus.Confirmed,
        };

        var washout = new PurchaseContractWashout
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            PriceFixationKey = fixation.Key,
            Sequence = 1,
            FixedVolume = washedFixedVolume,
            ContractPrice = contract.StandardPrice,
            MarketPrice = contract.StandardPrice + 1m,
            Amount = 1m,
            DueDate = DateTime.Today,
            Reason = "teste",
            Status = PurchaseContractWashoutStatus.Approved,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    /// <summary>
    /// F3 (revisão final): a fixação toda lavada tem <c>alreadyGenerated == false</c> (o
    /// provisório foi cancelado a zero), então sem o corte por washout o laço contava
    /// <c>Generated++</c> mesmo o gerador não escrevendo nada — no dry run ela ficava "pendente"
    /// para sempre.
    /// </summary>
    [Fact]
    public async Task A_fully_washed_out_fixation_is_skipped_in_dry_run_and_not_counted_as_generated()
    {
        await SeedApprovedFixedContractWithWashoutAsync(washedFixedVolume: 1_000m);

        var result = await Service().ExecuteAsync(
            null, DateTime.Today.AddYears(-1), DateTime.Today.AddDays(1), dryRun: true, "tester");

        Assert.Equal(1, result.SkippedFullyWashedOut);
        Assert.Equal(0, result.Generated);
        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task A_fully_washed_out_fixation_is_skipped_in_a_real_run_and_not_counted_as_generated()
    {
        await SeedApprovedFixedContractWithWashoutAsync(washedFixedVolume: 1_000m);

        var result = await Service().ExecuteAsync(
            null, DateTime.Today.AddYears(-1), DateTime.Today.AddDays(1), dryRun: false, "tester");

        Assert.Equal(1, result.SkippedFullyWashedOut);
        Assert.Equal(0, result.Generated);
        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task A_partially_washed_out_fixation_generates_the_discounted_amount()
    {
        await SeedApprovedFixedContractWithWashoutAsync(washedFixedVolume: 400m);

        var result = await Service().ExecuteAsync(
            null, DateTime.Today.AddYears(-1), DateTime.Today.AddDays(1), dryRun: false, "tester");

        Assert.Equal(1, result.Generated);
        Assert.Equal(0, result.SkippedFullyWashedOut);
        // (1.000 − 400) × 100
        Assert.Equal(60_000m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Running_the_backlog_twice_does_not_duplicate_a_partially_washed_out_fixation()
    {
        await SeedApprovedFixedContractWithWashoutAsync(washedFixedVolume: 400m);
        var service = Service();
        var from = DateTime.Today.AddYears(-1);
        var to = DateTime.Today.AddDays(1);

        await service.ExecuteAsync(null, from, to, dryRun: false, "tester");
        var second = await service.ExecuteAsync(null, from, to, dryRun: false, "tester");

        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.SkippedAlreadyGenerated);
        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task A_dry_run_counts_eligible_fixations_from_both_purchase_and_sales_contracts()
    {
        await SeedApprovedFixedContractAsync(cashFlowDate: DateTime.Today);
        await SeedApprovedFixedSalesContractAsync(cashFlowDate: DateTime.Today);

        var result = await Service().ExecuteAsync(
            branchCode: null,
            fromDate: DateTime.Today.AddYears(-1),
            toDate: DateTime.Today.AddDays(1),
            dryRun: true,
            userName: "tester");

        Assert.Equal(2, result.EligibleFixations);
        Assert.Equal(0, result.Generated);
        Assert.Empty(_db.Context.FinancialDocuments);
    }
}
