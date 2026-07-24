using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class PriceFixationsMutabilityTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsPriceFixationsUpdateService UpdateService() =>
        new(_db.Context,
            new SalesContractsFixedVolumeService(_db.Context),
            NullLogger<SalesContractsPriceFixationsUpdateService>.Instance);

    private SalesContractsPriceFixationDeleteService DeleteService() =>
        new(_db.Context,
            new SalesContractsFixedVolumeService(_db.Context),
            new SalesContractsChangeLogService(_db.Context),
            NullLogger<SalesContractsPriceFixationDeleteService>.Instance);

    private async Task<SalesContractPriceFixation> SeedAsync(PriceFixationStatus status)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-001",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 100_000m,
            FixedVolume = 20_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        var fixation = new SalesContractPriceFixation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            FixationVolume = 20_000m,
            FixationPrice = 2m,
            Status = status,
        };

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return fixation;
    }

    [Fact]
    public async Task Update_InApprovalFixation_Succeeds()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        var changes = new SalesContractPriceFixation
        {
            Key = fixation.Key,
            SalesContractKey = fixation.SalesContractKey,
            FixationVolume = 25_000m,
            FixationPrice = 3m,
            Status = PriceFixationStatus.InApproval,
        };

        await UpdateService().ExecuteAsync(fixation.Key, changes);

        var reloaded = await _db.Context.SalesContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);
        Assert.Equal(25_000m, reloaded.FixationVolume);
    }

    [Fact]
    public async Task Update_RecalculatesFixedVolume()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        var changes = new SalesContractPriceFixation
        {
            Key = fixation.Key,
            SalesContractKey = fixation.SalesContractKey,
            FixationVolume = 25_000m,
            FixationPrice = 3m,
            Status = PriceFixationStatus.InApproval,
        };

        await UpdateService().ExecuteAsync(fixation.Key, changes);

        var contract = await _db.Context.SalesContracts
            .AsNoTracking().SingleAsync(x => x.Key == fixation.SalesContractKey);
        Assert.Equal(25_000m, contract.FixedVolume);
    }

    [Fact]
    public async Task Update_CannotPromoteStatusViaPayload()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        var changes = new SalesContractPriceFixation
        {
            Key = fixation.Key,
            SalesContractKey = fixation.SalesContractKey,
            FixationVolume = 20_000m,
            FixationPrice = 2m,
            Status = PriceFixationStatus.Confirmed,
        };

        await UpdateService().ExecuteAsync(fixation.Key, changes);

        var reloaded = await _db.Context.SalesContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);
        Assert.Equal(PriceFixationStatus.InApproval, reloaded.Status);
    }

    [Fact]
    public async Task Update_ConfirmedFixation_Throws()
    {
        var fixation = await SeedAsync(PriceFixationStatus.Confirmed);

        var changes = new SalesContractPriceFixation
        {
            Key = fixation.Key,
            FixationVolume = 25_000m,
            FixationPrice = 3m,
            Status = PriceFixationStatus.Confirmed,
        };

        await Assert.ThrowsAsync<ApplicationException>(() =>
            UpdateService().ExecuteAsync(fixation.Key, changes));
    }

    [Fact]
    public async Task Delete_InApprovalFixation_Succeeds_AndReleasesVolume()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        await DeleteService().ExecuteAsync(fixation.Key, "tester");

        Assert.False(await _db.Context.SalesContractsPriceFixations
            .AnyAsync(x => x.Key == fixation.Key));

        var contract = await _db.Context.SalesContracts
            .AsNoTracking().SingleAsync(x => x.Key == fixation.SalesContractKey);
        Assert.Equal(0m, contract.FixedVolume);
    }

    [Fact]
    public async Task Delete_ConfirmedFixation_Throws()
    {
        var fixation = await SeedAsync(PriceFixationStatus.Confirmed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            DeleteService().ExecuteAsync(fixation.Key, "tester"));
    }
}
