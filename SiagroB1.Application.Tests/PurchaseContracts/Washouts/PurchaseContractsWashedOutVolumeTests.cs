using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashedOutVolumeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task Recalculate_counts_only_in_approval_and_approved_washouts()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.PurchaseContractsWashouts.AddRange(
            WashoutTestData.Washout(contract, fixation, fixedVolume: 1_000m, sequence: 1),
            WashoutTestData.Washout(contract, unfixedVolume: 2_000m, status: PurchaseContractWashoutStatus.Approved, sequence: 2),
            WashoutTestData.Washout(contract, unfixedVolume: 5_000m, status: PurchaseContractWashoutStatus.Rejected, sequence: 3),
            WashoutTestData.Washout(contract, unfixedVolume: 7_000m, status: PurchaseContractWashoutStatus.Reversed, sequence: 4));
        await _db.Context.SaveChangesAsync();

        await new PurchaseContractsWashedOutVolumeService(_db.Context).RecalculateAsync(contract);

        Assert.Equal(3_000m, contract.WashedOutVolume);
        Assert.Equal(2_000m, contract.WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Active_volumes_can_exclude_one_washout()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        var kept = WashoutTestData.Washout(contract, unfixedVolume: 1_000m, sequence: 1);
        var excluded = WashoutTestData.Washout(contract, unfixedVolume: 4_000m, sequence: 2);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsWashouts.AddRange(kept, excluded);
        await _db.Context.SaveChangesAsync();

        var (total, unfixed) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(_db.Context, contract.Key, excluded.Key);

        Assert.Equal(1_000m, total);
        Assert.Equal(1_000m, unfixed);
    }

    [Fact]
    public async Task Fixed_volume_per_fixation_distinguishes_active_from_approved()
    {
        var contract = WashoutTestData.Contract();
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        var pending = WashoutTestData.Washout(contract, fixation, fixedVolume: 3_000m, sequence: 1);
        var approved = WashoutTestData.Washout(contract, fixation, fixedVolume: 4_000m,
            status: PurchaseContractWashoutStatus.Approved, sequence: 2);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.PurchaseContractsWashouts.AddRange(pending, approved);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(7_000m, await PurchaseContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(3_000m, await PurchaseContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key, approved.Key));
        Assert.Equal(4_000m, await PurchaseContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(0m, await PurchaseContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(_db.Context, fixation.Key, approved.Key));
    }
}
