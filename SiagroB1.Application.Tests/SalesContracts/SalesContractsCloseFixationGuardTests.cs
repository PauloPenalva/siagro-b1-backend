using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsCloseFixationGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsCloseService CloseService() =>
        new(_db.Context, new SalesContractsFixedVolumeService(_db.Context));

    private static SalesContract NewContract(ContractStatus status, ContractType type) => new()
    {
        Key = Guid.NewGuid(),
        Code = "SC-001",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = 100_000m,
        Type = type,
        Status = status,
    };

    private async Task<SalesContract> ReloadAsync(Guid key) =>
        await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    private async Task<SalesContract> SeedPafAsync(
        decimal totalVolume,
        decimal releasedQuantity,
        decimal shippedQuantity,
        params (decimal Volume, PriceFixationStatus Status)[] fixations)
    {
        var contract = NewContract(ContractStatus.Approved, ContractType.ToBeDetermined);
        contract.TotalVolume = totalVolume;

        _db.Context.SalesContracts.Add(contract);

        if (releasedQuantity > 0)
        {
            _db.Context.SalesShipmentReleases.Add(new SalesShipmentRelease
            {
                Key = Guid.NewGuid(),
                SalesContractKey = contract.Key,
                DeliveryLocationCode = "01",
                ReleasedQuantity = releasedQuantity,
                ShippedQuantity = shippedQuantity,
                Status = ReleaseStatus.Actived,
            });
        }

        foreach (var (volume, status) in fixations)
        {
            _db.Context.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = contract.Key,
                FixationVolume = volume,
                FixationPrice = 2m,
                Status = status,
            });
        }

        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeFullyConfirmed_Succeeds()
    {
        var sc = await SeedPafAsync(100_000m, 60_000m, 60_000m, (60_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeNotFullyFixed_Throws()
    {
        var sc = await SeedPafAsync(100_000m, 60_000m, 60_000m, (40_000m, PriceFixationStatus.Confirmed));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(sc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeCoveredOnlyByInApproval_Throws()
    {
        var sc = await SeedPafAsync(100_000m, 60_000m, 60_000m, (60_000m, PriceFixationStatus.InApproval));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(sc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_WithPendingFixation_Throws()
    {
        var sc = await SeedPafAsync(100_000m, 60_000m, 60_000m,
            (60_000m, PriceFixationStatus.Confirmed),
            (10_000m, PriceFixationStatus.InApproval));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(sc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_ReleasedButNotShipped_DoesNotBlock()
    {
        // Liberação ativa de 60.000 com apenas 10.000 romaneados: só os 10.000 exigem preço.
        var sc = await SeedPafAsync(100_000m, 60_000m, 10_000m,
            (10_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }

    [Fact]
    public async Task Close_FixedContract_IgnoresFixationGuard()
    {
        var contract = NewContract(ContractStatus.Approved, ContractType.Fixed);
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        await CloseService().ExecuteAsync(contract.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(contract.Key)).Status);
    }

    [Fact]
    public async Task Close_NonApprovedContract_Throws()
    {
        var contract = NewContract(ContractStatus.Draft, ContractType.ToBeDetermined);
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CloseService().ExecuteAsync(contract.Key, "tester"));
    }
}
