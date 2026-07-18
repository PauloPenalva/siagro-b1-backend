using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// A query reexpressa em SQL a regra de <see cref="PurchaseContract.TotalAvailableToRelease"/>
/// e precisa acompanhá-la: liberação cancelada consome apenas o que foi romaneado.
/// </summary>
public class PurchaseContractsGetShipmentReleasesAvailableServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsGetShipmentReleasesAvailableService Service() =>
        new(_db, NullLogger<PurchaseContractsGetShipmentReleasesAvailableService>.Instance);

    private async Task<PurchaseContract> SeedAsync(
        decimal totalVolume, decimal released, decimal shipped, ReleaseStatus status)
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            TotalVolume = totalVolume, Status = ContractStatus.Approved,
        };
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = pc.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = released, ShippedQuantity = shipped, Status = status,
        });
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    [Fact]
    public async Task CancelledWithMovement_ContractListedWithRemainingBalance()
    {
        var pc = await SeedAsync(1000m, released: 1000m, shipped: 300m, status: ReleaseStatus.Cancelled);

        var result = await Service().Query().ToListAsync();

        var listed = Assert.Single(result);
        Assert.Equal(pc.Key, listed.Key);
    }

    [Fact]
    public async Task FullyReleasedActive_ContractNotListed()
    {
        await SeedAsync(1000m, released: 1000m, shipped: 300m, status: ReleaseStatus.Actived);

        Assert.Empty(await Service().Query().ToListAsync());
    }

    [Fact]
    public async Task CancelledWithFullShipment_ContractNotListed()
    {
        await SeedAsync(1000m, released: 1000m, shipped: 1000m, status: ReleaseStatus.Cancelled);

        Assert.Empty(await Service().Query().ToListAsync());
    }

    [Fact]
    public async Task CancelledWithoutMovement_ContractListed()
    {
        await SeedAsync(1000m, released: 1000m, shipped: 0m, status: ReleaseStatus.Cancelled);

        Assert.Single(await Service().Query().ToListAsync());
    }
}
