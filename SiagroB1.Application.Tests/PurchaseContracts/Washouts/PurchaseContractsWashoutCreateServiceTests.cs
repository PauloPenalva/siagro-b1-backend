using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashoutCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsWashoutCreateService Service() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>Preço fixo: 100.000 kg com a fixação automática de 100.000 @ 2,50.</summary>
    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedFixAsync(
        Action<PurchaseContract>? tweak = null)
    {
        var contract = WashoutTestData.Contract();
        contract.FixedVolume = 100_000m;
        tweak?.Invoke(contract);
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    /// <summary>A fixar: 100.000 kg, 30.000 fixados @ 2,50.</summary>
    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedPafAsync()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.FixedVolume = 30_000m;
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    private static PurchaseContractWashout Input(
        Guid? fixationKey, decimal fixedVolume, decimal unfixedVolume = 0m,
        decimal marketPrice = 2.75m, decimal penaltyAmount = 1_000m,
        DateTime? dueDate = null, string? reason = "Produtor sem produto", bool noDueDate = false) => new()
    {
        PriceFixationKey = fixationKey,
        FixedVolume = fixedVolume,
        UnfixedVolume = unfixedVolume,
        MarketPrice = marketPrice,
        PenaltyAmount = penaltyAmount,
        DueDate = noDueDate ? null : dueDate ?? new DateTime(2026, 10, 31),
        Reason = reason,
    };

    private async Task<ApplicationException> RefusesAsync(Guid contractKey, PurchaseContractWashout input, string fragment)
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(contractKey, input, "tester"));
        Assert.Contains(fragment, error.Message, StringComparison.OrdinalIgnoreCase);
        return error;
    }

    [Fact]
    public async Task Creates_in_approval_with_amount_sequence_log_notification_and_reserved_volume()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 10_000m), "tester");

        Assert.Equal(PurchaseContractWashoutStatus.InApproval, washout.Status);
        Assert.Equal(1, washout.Sequence);
        Assert.Equal(2.5m, washout.ContractPrice);
        Assert.Equal(3_500m, washout.Amount);
        Assert.Equal("tester", washout.CreatedBy);

        var reloaded = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(10_000m, reloaded.WashedOutVolume);
        Assert.Equal(0m, reloaded.WashedOutUnfixedVolume);

        Assert.Equal(ContractChangeLogFields.Washout, _db.Context.PurchaseContractsChangeLogs.Single().Field);
        Assert.Equal(NotificationEventType.WashoutCreated, _db.Context.NotificationOutboxMessages.Single().EventType);
    }

    [Fact]
    public async Task Second_washout_gets_the_next_sequence()
    {
        var (contract, fixation) = await SeedFixAsync();

        await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 1_000m), "tester");
        var second = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 1_000m), "tester");

        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public async Task Market_below_contract_charges_only_the_penalty()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 10_000m, marketPrice: 2.0m), "tester");

        Assert.Equal(1_000m, washout.Amount);
    }

    [Fact]
    public async Task Zero_amount_needs_no_due_date()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key,
            Input(fixation.Key, 10_000m, marketPrice: 2.0m, penaltyAmount: 0m, noDueDate: true), "tester");

        Assert.Equal(0m, washout.Amount);
        Assert.Null(washout.DueDate);
    }

    [Fact]
    public async Task Paf_unfixed_only_washout_keeps_no_fixation_and_charges_only_the_penalty()
    {
        var (contract, fixation) = await SeedPafAsync();

        var washout = await Service().ExecuteAsync(contract.Key,
            Input(fixation.Key, 0m, unfixedVolume: 20_000m, marketPrice: 9m), "tester");

        Assert.Null(washout.PriceFixationKey);
        Assert.Equal(0m, washout.ContractPrice);
        Assert.Equal(1_000m, washout.Amount);
        Assert.Equal(20_000m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Refuses_a_contract_that_is_not_approved()
    {
        var (contract, fixation) = await SeedFixAsync(c => c.Status = ContractStatus.Draft);
        await RefusesAsync(contract.Key, Input(fixation.Key, 1_000m), "aprovado");
    }

    [Fact]
    public async Task Refuses_zero_volume()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 0m), "volume");
    }

    [Fact]
    public async Task Refuses_a_missing_reason()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 1_000m, reason: " "), "motivo");
    }

    [Fact]
    public async Task Refuses_unfixed_volume_on_a_fixed_price_contract()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 0m, unfixedVolume: 1_000m), "preço fixo");
    }

    [Fact]
    public async Task Refuses_fixed_volume_without_a_fixation()
    {
        var (contract, _) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(null, 1_000m), "fixação");
    }

    [Fact]
    public async Task Refuses_a_fixation_of_another_contract()
    {
        var (contract, _) = await SeedFixAsync();
        var other = WashoutTestData.Contract();
        other.Code = "PC-002";
        var otherFixation = WashoutTestData.Fixation(other, 100_000m);
        _db.Context.PurchaseContracts.Add(other);
        _db.Context.PurchaseContractsPriceFixations.Add(otherFixation);
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(otherFixation.Key, 1_000m), "não pertence");
    }

    [Fact]
    public async Task Refuses_a_fixation_that_is_not_confirmed()
    {
        var (contract, _) = await SeedPafAsync();
        var pending = WashoutTestData.Fixation(contract, 10_000m, status: PriceFixationStatus.InApproval);
        _db.Context.PurchaseContractsPriceFixations.Add(pending);
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(pending.Key, 1_000m), "confirmada");
    }

    [Fact]
    public async Task Refuses_fixed_volume_above_the_fixation_balance()
    {
        var (contract, fixation) = await SeedPafAsync();
        _db.Context.PurchaseContractsWashouts.Add(WashoutTestData.Washout(contract, fixation, fixedVolume: 25_000m,
            status: PurchaseContractWashoutStatus.Approved));
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m), "saldo da fixação");
    }

    [Fact]
    public async Task Refuses_unfixed_volume_above_the_volume_available_to_pricing()
    {
        var (contract, fixation) = await SeedPafAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 0m, unfixedVolume: 80_000m), "a fixar");
    }

    [Fact]
    public async Task Refuses_volume_above_the_physical_balance()
    {
        var (contract, fixation) = await SeedFixAsync(c => c.AllocatedVolume = 95_000m);
        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m), "saldo físico");
    }

    [Fact]
    public async Task Refuses_volume_above_the_balance_not_yet_released()
    {
        var (contract, fixation) = await SeedFixAsync();
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 95_000m,
            Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m), "não liberado");
    }

    [Fact]
    public async Task Refuses_a_positive_amount_without_due_date()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m, noDueDate: true), "vencimento");
    }

    [Fact]
    public async Task Refuses_a_reason_longer_than_500_characters()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(
            contract.Key, Input(fixation.Key, 10_000m, reason: new string('a', 501)), "500 caracteres");
    }

    /// <summary>
    /// F2 (revisão final): PAF 100.000, F1 30.000 Confirmed, 20.000 já entregues. O saldo com
    /// preço ainda não entregue é 10.000 — lavar os 30.000 fixados deixaria os 20.000 entregues
    /// sem preço e sem título a pagar.
    /// </summary>
    private async Task<PurchaseContract> AddDeliveryAsync(PurchaseContract contract, decimal shippedQuantity)
    {
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = shippedQuantity,
            ShippedQuantity = shippedQuantity,
            Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Refuses_fixed_volume_above_the_priced_undelivered_volume()
    {
        var (contract, fixation) = await SeedPafAsync();
        await AddDeliveryAsync(contract, 20_000m);

        await RefusesAsync(contract.Key, Input(fixation.Key, 30_000m), "com preço ainda não entregue");
    }

    [Fact]
    public async Task Accepts_fixed_volume_within_the_priced_undelivered_volume()
    {
        var (contract, fixation) = await SeedPafAsync();
        await AddDeliveryAsync(contract, 20_000m);

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 10_000m), "tester");

        Assert.Equal(10_000m, washout.FixedVolume);
    }
}
