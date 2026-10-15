using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §4: quem passa a descontar o lavado.</summary>
public class SalesWashoutBalanceConsumersTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<SalesContract> SeedAsync(SalesContract contract, params object[] children)
    {
        _db.Context.SalesContracts.Add(contract);
        foreach (var child in children) _db.Context.Add(child);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Recalculate_sums_only_active_washouts()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        await SeedAsync(contract,
            SalesWashoutTestData.Washout(contract, unfixedVolume: 100m, sequence: 1),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 50m, status: SalesContractWashoutStatus.Approved, sequence: 2),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 30m, status: SalesContractWashoutStatus.Rejected, sequence: 3),
            SalesWashoutTestData.Washout(contract, unfixedVolume: 20m, status: SalesContractWashoutStatus.Reversed, sequence: 4));

        await new SalesContractsWashedOutVolumeService(_db.Context).RecalculateAsync(contract);

        Assert.Equal(150m, contract.WashedOutVolume);
        Assert.Equal(150m, contract.WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Approved_fixed_volume_ignores_in_approval_and_the_excluded_washout()
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        var approved = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Approved, sequence: 1);
        var pending = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 40m, sequence: 2);
        await SeedAsync(contract, fixation, approved, pending);

        Assert.Equal(100m, await SalesContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(140m, await SalesContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(40m, await SalesContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key, approved.Key));
    }

    [Fact]
    public async Task Fully_washed_contract_leaves_the_release_selector()
    {
        var contract = SalesWashoutTestData.Contract();
        contract.WashedOutVolume = 1_000m;
        await SeedAsync(contract);

        var service = new SalesContractsGetShipmentReleasesAvailableService(
            _db, NullLogger<SalesContractsGetShipmentReleasesAvailableService>.Instance);

        Assert.DoesNotContain(await service.Query().ToListAsync(), c => c.Key == contract.Key);
    }

    [Fact]
    public async Task Price_fixation_cannot_use_the_washed_unfixed_volume()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.WashedOutUnfixedVolume = 800m;
        await SeedAsync(contract, SalesWashoutTestData.Washout(contract, unfixedVolume: 800m));

        var service = new SalesContractsPriceFixationCreateService(
            _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
            TestNotificationOutbox.For(_db.Context), NullLogger<SalesContractsPriceFixationCreateService>.Instance);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(contract.Key,
            new SalesContractPriceFixation { FixationVolume = 300m, FixationPrice = 100m }, "tester"));

        Assert.Contains("Volume excede o saldo disponível para fixação", error.Message);
    }

    [Fact]
    public async Task Totals_expose_the_washed_volume()
    {
        var contract = SalesWashoutTestData.Contract();
        contract.WashedOutVolume = 250m;
        await SeedAsync(contract);

        var totals = await new SalesContractsTotalsService(_db.Context).GetTotals(contract.Key);

        Assert.Equal(250m, totals.WashedOutVolume);
    }
}
