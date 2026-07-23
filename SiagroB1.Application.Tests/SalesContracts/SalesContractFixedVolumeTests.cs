using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractFixedVolumeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<SalesContract> SeedAsync(params (decimal Volume, PriceFixationStatus Status)[] fixations)
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
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.SalesContracts.Add(contract);

        foreach (var (volume, status) in fixations)
        {
            _db.Context.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = contract.Key,
                FixationVolume = volume,
                FixationPrice = 1m,
                Status = status,
            });
        }

        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Recalculate_SumsInApprovalAndConfirmed()
    {
        var contract = await SeedAsync(
            (30_000m, PriceFixationStatus.Confirmed),
            (20_000m, PriceFixationStatus.InApproval));

        var result = await new SalesContractsFixedVolumeService(_db.Context)
            .RecalculateAsync(contract);

        Assert.Equal(50_000m, result);
        Assert.Equal(50_000m, contract.FixedVolume);
    }

    [Fact]
    public async Task Recalculate_IgnoresCanceledAndRejected()
    {
        var contract = await SeedAsync(
            (30_000m, PriceFixationStatus.Confirmed),
            (20_000m, PriceFixationStatus.Canceled),
            (10_000m, PriceFixationStatus.Rejected));

        var result = await new SalesContractsFixedVolumeService(_db.Context)
            .RecalculateAsync(contract);

        Assert.Equal(30_000m, result);
    }

    [Fact]
    public async Task ConfirmedVolume_CountsConfirmedOnly()
    {
        var contract = await SeedAsync(
            (30_000m, PriceFixationStatus.Confirmed),
            (20_000m, PriceFixationStatus.InApproval));

        var confirmed = await new SalesContractsFixedVolumeService(_db.Context)
            .ConfirmedVolumeAsync(contract.Key);

        Assert.Equal(30_000m, confirmed);
    }

    [Fact]
    public async Task AvailableVolumeToPricing_DerivesFromPersistedFixedVolume()
    {
        var contract = await SeedAsync((30_000m, PriceFixationStatus.Confirmed));

        await new SalesContractsFixedVolumeService(_db.Context).RecalculateAsync(contract);
        await _db.Context.SaveChangesAsync();

        // Recarrega SEM Include das fixações: o valor tem que sobreviver.
        var reloaded = await _db.Context.SalesContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(30_000m, reloaded.FixedVolume);
        Assert.Equal(70_000m, reloaded.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task DeliveredVolume_SumsShippedQuantity_NotReleasedQuantity()
    {
        var contract = await SeedAsync();

        _db.Context.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 60_000m,
            ShippedQuantity = 10_000m,
            Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();

        var delivered = await new SalesContractsFixedVolumeService(_db.Context)
            .DeliveredVolumeAsync(contract.Key);

        Assert.Equal(10_000m, delivered);
    }

    [Fact]
    public async Task ConfirmedUnitPrice_WeightedAverageOfConfirmed()
    {
        var contract = await SeedAsync();
        _db.Context.SalesContractsPriceFixations.AddRange(
            new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = contract.Key,
                FixationVolume = 30_000m,
                FixationPrice = 2m,
                Status = PriceFixationStatus.Confirmed,
            },
            new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = contract.Key,
                FixationVolume = 10_000m,
                FixationPrice = 6m,
                Status = PriceFixationStatus.Confirmed,
            },
            // InApproval não conta no preço.
            new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = contract.Key,
                FixationVolume = 40_000m,
                FixationPrice = 100m,
                Status = PriceFixationStatus.InApproval,
            });
        await _db.Context.SaveChangesAsync();

        var price = await new SalesContractsFixedVolumeService(_db.Context)
            .ConfirmedUnitPriceAsync(contract.Key, fallbackPrice: 999m);

        // (30.000×2 + 10.000×6) / 40.000 = 120.000 / 40.000 = 3
        Assert.Equal(3m, price);
    }

    [Fact]
    public async Task ConfirmedUnitPrice_NoConfirmedFixation_FallsBackToPrice()
    {
        var contract = await SeedAsync((40_000m, PriceFixationStatus.InApproval));

        var price = await new SalesContractsFixedVolumeService(_db.Context)
            .ConfirmedUnitPriceAsync(contract.Key, fallbackPrice: 123.45m);

        Assert.Equal(123.45m, price);
    }

    [Fact]
    public async Task PendingQueue_ExcludesFixedContractFixations()
    {
        var fixedContract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-FIXO",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 600_000m,
            Type = ContractType.Fixed,
            Status = ContractStatus.Approved,
        };
        var pafContract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-PAF",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.SalesContracts.AddRange(fixedContract, pafContract);
        _db.Context.SalesContractsPriceFixations.AddRange(
            new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = fixedContract.Key,
                FixationVolume = 600_000m,
                FixationPrice = 1m,
                Status = PriceFixationStatus.InApproval,
            },
            new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = pafContract.Key,
                FixationVolume = 30_000m,
                FixationPrice = 2.5m,
                Status = PriceFixationStatus.InApproval,
            });
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var pending = await new SalesContractsPriceFixationsGetService(
                _db.Context,
                NullLogger<SalesContractsPriceFixationsGetService>.Instance)
            .QueryPending()
            .ToListAsync();

        Assert.Single(pending);
        Assert.Equal(pafContract.Key, pending[0].SalesContractKey);
    }
}
