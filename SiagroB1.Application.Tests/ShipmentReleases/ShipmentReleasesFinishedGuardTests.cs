using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesFinishedGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedFinishedContractAsync()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-FIN",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 1000m,
            Status = ContractStatus.Finished,
        };
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private async Task<ShipmentRelease> SeedReleaseAsync(Guid contractKey, ReleaseStatus status)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contractKey,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    [Fact]
    public async Task Delete_ContractFinished_ThrowsAndKeepsRelease()
    {
        var pc = await SeedFinishedContractAsync();
        var sr = await SeedReleaseAsync(pc.Key, ReleaseStatus.Pending);

        var service = new ShipmentReleasesDeleteService(_db, NullLogger<ShipmentReleasesDeleteService>.Instance);

        await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(sr.Key));

        Assert.Equal(1, await _db.Context.ShipmentReleases.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Cancel_ContractFinished_ThrowsAndKeepsStatus()
    {
        var pc = await SeedFinishedContractAsync();
        var sr = await SeedReleaseAsync(pc.Key, ReleaseStatus.Actived);

        var service = new ShipmentReleasesCancelationService(
            _db.Context,
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            NullLogger<ShipmentReleasesCancelationService>.Instance);

        await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(sr.Key, "maria", "troca de armazém"));

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == sr.Key);
        Assert.Equal(ReleaseStatus.Actived, reloaded.Status);
    }
}
