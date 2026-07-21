using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFixedVolumeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedAsync(params (decimal Volume, PriceFixationStatus Status)[] fixations)
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
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.PurchaseContracts.Add(contract);

        foreach (var (volume, status) in fixations)
        {
            _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
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

        var result = await new PurchaseContractsFixedVolumeService(_db.Context)
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

        var result = await new PurchaseContractsFixedVolumeService(_db.Context)
            .RecalculateAsync(contract);

        Assert.Equal(30_000m, result);
    }

    [Fact]
    public async Task ConfirmedVolume_CountsConfirmedOnly()
    {
        var contract = await SeedAsync(
            (30_000m, PriceFixationStatus.Confirmed),
            (20_000m, PriceFixationStatus.InApproval));

        var confirmed = await new PurchaseContractsFixedVolumeService(_db.Context)
            .ConfirmedVolumeAsync(contract.Key);

        Assert.Equal(30_000m, confirmed);
    }

    [Fact]
    public async Task AvailableVolumeToPricing_DerivesFromPersistedFixedVolume()
    {
        var contract = await SeedAsync((30_000m, PriceFixationStatus.Confirmed));

        await new PurchaseContractsFixedVolumeService(_db.Context).RecalculateAsync(contract);
        await _db.Context.SaveChangesAsync();

        // Recarrega SEM Include das fixações: o valor tem que sobreviver.
        var reloaded = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(30_000m, reloaded.FixedVolume);
        Assert.Equal(70_000m, reloaded.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task DeliveredVolume_SumsShippedQuantity_NotReleasedQuantity()
    {
        var contract = await SeedAsync();

        // Liberação ativa: 60.000 liberados, apenas 10.000 efetivamente romaneados.
        // TotalShipmentReleases somaria ConsumedQuantity (= ReleasedQuantity = 60.000);
        // o volume ENTREGUE é 10.000.
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 60_000m,
            ShippedQuantity = 10_000m,
            Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();

        var delivered = await new PurchaseContractsFixedVolumeService(_db.Context)
            .DeliveredVolumeAsync(contract.Key);

        Assert.Equal(10_000m, delivered);
    }
}
