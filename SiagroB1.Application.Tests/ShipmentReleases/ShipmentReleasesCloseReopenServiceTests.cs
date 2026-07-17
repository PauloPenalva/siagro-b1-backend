using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesCloseReopenServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<ShipmentRelease> SeedAsync(ReleaseStatus status)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private async Task<ShipmentRelease> ReloadAsync(Guid key) =>
        await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    [Theory]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    public async Task Close_ActivedOrPaused_BecomesCompleted(ReleaseStatus from)
    {
        var sr = await SeedAsync(from);

        await new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(sr.Key, "paulo.penalva");

        var reloaded = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Completed, reloaded.Status);
        Assert.Equal("paulo.penalva", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Close_PendingRelease_Throws()
    {
        var sr = await SeedAsync(ReleaseStatus.Pending);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(sr.Key, "tester"));
    }

    [Fact]
    public async Task Reopen_Completed_BecomesActived()
    {
        var sr = await SeedAsync(ReleaseStatus.Completed);

        await new ShipmentReleasesReopenService(_db.Context).ExecuteAsync(sr.Key, "tester");

        Assert.Equal(ReleaseStatus.Actived, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Reopen_NotCompleted_Throws()
    {
        var sr = await SeedAsync(ReleaseStatus.Actived);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ShipmentReleasesReopenService(_db.Context).ExecuteAsync(sr.Key, "tester"));
    }
}
