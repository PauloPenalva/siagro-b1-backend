using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.1.</summary>
public class SalesContractsWashoutCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsWashoutCreateService Service() => new(
        _db.Context,
        new SalesContractsWashedOutVolumeService(_db.Context),
        new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>Preço fixo: 1.000 KG com a fixação automática de 1.000 @ 100.</summary>
    private async Task<(SalesContract Contract, SalesContractPriceFixation Fixation)> SeedFixAsync()
    {
        var contract = SalesWashoutTestData.Contract();
        contract.FixedVolume = 1_000m;
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    /// <summary>A fixar: 1.000 KG, 300 fixados @ 100.</summary>
    private async Task<(SalesContract Contract, SalesContractPriceFixation Fixation)> SeedPafAsync()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.FixedVolume = 300m;
        var fixation = SalesWashoutTestData.Fixation(contract, 300m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    private static SalesContractWashout Input(
        Guid? fixationKey, decimal fixedVolume, decimal unfixedVolume = 0m,
        decimal marketPrice = 90m, decimal penaltyAmount = 500m, bool noDueDate = false,
        string? reason = "Cliente desistiu") => new()
    {
        PriceFixationKey = fixationKey,
        FixedVolume = fixedVolume,
        UnfixedVolume = unfixedVolume,
        MarketPrice = marketPrice,
        PenaltyAmount = penaltyAmount,
        DueDate = noDueDate ? null : new DateTime(2026, 10, 31),
        Reason = reason,
    };

    private async Task RefusesAsync(Guid contractKey, SalesContractWashout input, string fragment)
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(contractKey, input, "tester"));
        Assert.Contains(fragment, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creates_in_approval_with_inverted_amount_sequence_log_notification_and_reserved_volume()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m), "tester");

        Assert.Equal(SalesContractWashoutStatus.InApproval, washout.Status);
        Assert.Equal(1, washout.Sequence);
        Assert.Equal(100m, washout.ContractPrice);
        Assert.Equal(1_500m, washout.Amount);   // (100 − 90) × 100 + 500

        var reloaded = await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(100m, reloaded.WashedOutVolume);
        Assert.Equal(0m, reloaded.WashedOutUnfixedVolume);
        Assert.Equal(ContractChangeLogFields.Washout, _db.Context.SalesContractsChangeLogs.Single().Field);
        Assert.Equal(NotificationEventType.WashoutCreated, _db.Context.NotificationOutboxMessages.Single().EventType);
    }

    [Fact]
    public async Task Second_washout_gets_the_next_sequence()
    {
        var (contract, fixation) = await SeedFixAsync();
        await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m), "tester");

        var second = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 50m), "tester");

        Assert.Equal(2, second.Sequence);
        Assert.Equal(150m, (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutVolume);
    }

    [Fact]
    public async Task Market_above_the_contract_charges_only_the_penalty()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m, marketPrice: 120m), "tester");

        Assert.Equal(500m, washout.Amount);
    }

    [Fact]
    public async Task Fixed_volume_already_billed_is_refused()
    {
        // PAF: 300 fixados e confirmados, 200 já faturados (ledger). Sobram 100 com preço não entregues, embora o
        // saldo físico (800) e o da fixação (300) aceitassem 150.
        var (contract, fixation) = await SeedPafAsync();
        var (invoice, allocation) = SalesWashoutTestData.Billed(contract, 200m);
        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.SalesContractsAllocations.Add(allocation);
        var tracked = await _db.Context.SalesContracts.SingleAsync(c => c.Key == contract.Key);
        tracked.AllocatedVolume = 200m;
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 150m),
            "Volume fixado excede o volume com preço ainda não entregue");
    }

    [Fact]
    public async Task Fixed_volume_within_the_unbilled_priced_volume_is_accepted()
    {
        var (contract, fixation) = await SeedPafAsync();
        var (invoice, allocation) = SalesWashoutTestData.Billed(contract, 200m);
        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.SalesContractsAllocations.Add(allocation);
        await _db.Context.SaveChangesAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m), "tester");

        Assert.Equal(100m, washout.FixedVolume);
    }

    [Fact]
    public async Task Paf_washes_fixed_and_unfixed_volumes()
    {
        var (contract, fixation) = await SeedPafAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 100m, unfixedVolume: 200m), "tester");

        Assert.Equal(1_500m, washout.Amount);   // o não fixado entra só com a multa
        var reloaded = await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(300m, reloaded.WashedOutVolume);
        Assert.Equal(200m, reloaded.WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Unfixed_only_washout_does_not_hold_the_fixation()
    {
        var (contract, fixation) = await SeedPafAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 0m, unfixedVolume: 100m), "tester");

        Assert.Null(washout.PriceFixationKey);
        Assert.Equal(0m, washout.ContractPrice);
    }

    [Fact]
    public async Task Unfixed_volume_above_the_balance_to_price_is_refused()
    {
        var (contract, _) = await SeedPafAsync();

        await RefusesAsync(contract.Key, Input(null, 0m, unfixedVolume: 701m), "Volume não fixado excede o saldo a fixar");
    }

    [Fact]
    public async Task Fixed_contract_refuses_unfixed_volume()
    {
        var (contract, fixation) = await SeedFixAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10m, unfixedVolume: 10m),
            "Contrato de preço fixo não tem volume a fixar");
    }

    [Fact]
    public async Task Volume_above_the_unreleased_balance_is_refused()
    {
        var (contract, fixation) = await SeedFixAsync();
        _db.Context.SalesShipmentReleases.Add(SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 950m));
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 100m), "excede o saldo não liberado");
    }

    [Fact]
    public async Task Amount_above_zero_requires_a_due_date()
    {
        var (contract, fixation) = await SeedFixAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 100m, noDueDate: true), "Informe o vencimento do título a receber.");
    }

    [Fact]
    public async Task Not_approved_contract_is_refused()
    {
        var contract = SalesWashoutTestData.Contract(status: ContractStatus.Finished);
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(null, 0m, unfixedVolume: 1m), "Contrato precisa estar aprovado para movimentar washout.");
    }

    [Fact]
    public async Task Missing_reason_is_refused()
    {
        var (contract, fixation) = await SeedFixAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10m, reason: "  "), "Informe o motivo do washout.");
    }
}
