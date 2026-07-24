using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsMutabilityTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsPriceFixationsUpdateService UpdateService() =>
        new(_db.Context,
            new PurchaseContractsFixedVolumeService(_db.Context),
            NullLogger<PurchaseContractsPriceFixationsUpdateService>.Instance);

    private PurchaseContractsPriceFixationDeleteService DeleteService() =>
        new(_db.Context,
            new PurchaseContractsFixedVolumeService(_db.Context),
            new PurchaseContractsChangeLogService(_db.Context),
            NullLogger<PurchaseContractsPriceFixationDeleteService>.Instance);

    private async Task<PurchaseContractPriceFixation> SeedAsync(PriceFixationStatus status)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            FixedVolume = 20_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 20_000m,
            FixationPrice = 2m,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return fixation;
    }

    [Fact]
    public async Task Update_InApprovalFixation_Succeeds()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        var changes = new PurchaseContractPriceFixation
        {
            Key = fixation.Key,
            PurchaseContractKey = fixation.PurchaseContractKey,
            FixationVolume = 25_000m,
            FixationPrice = 3m,
            Status = PriceFixationStatus.InApproval,
        };

        await UpdateService().ExecuteAsync(fixation.Key, changes);

        var reloaded = await _db.Context.PurchaseContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);
        Assert.Equal(25_000m, reloaded.FixationVolume);
    }

    [Fact]
    public async Task Update_RecalculatesFixedVolume()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        var changes = new PurchaseContractPriceFixation
        {
            Key = fixation.Key,
            PurchaseContractKey = fixation.PurchaseContractKey,
            FixationVolume = 25_000m,
            FixationPrice = 3m,
            Status = PriceFixationStatus.InApproval,
        };

        await UpdateService().ExecuteAsync(fixation.Key, changes);

        var contract = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == fixation.PurchaseContractKey);
        Assert.Equal(25_000m, contract.FixedVolume);
    }

    [Fact]
    public async Task Update_CannotPromoteStatusViaPayload()
    {
        // O payload do cliente não pode promover a fixação a Confirmed,
        // contornando a aprovação da diretoria.
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        var changes = new PurchaseContractPriceFixation
        {
            Key = fixation.Key,
            PurchaseContractKey = fixation.PurchaseContractKey,
            FixationVolume = 20_000m,
            FixationPrice = 2m,
            Status = PriceFixationStatus.Confirmed,
        };

        await UpdateService().ExecuteAsync(fixation.Key, changes);

        var reloaded = await _db.Context.PurchaseContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);
        Assert.Equal(PriceFixationStatus.InApproval, reloaded.Status);
    }

    [Fact]
    public async Task Update_ConfirmedFixation_Throws()
    {
        var fixation = await SeedAsync(PriceFixationStatus.Confirmed);

        var changes = new PurchaseContractPriceFixation
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

        Assert.False(await _db.Context.PurchaseContractsPriceFixations
            .AnyAsync(x => x.Key == fixation.Key));

        var contract = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == fixation.PurchaseContractKey);
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
