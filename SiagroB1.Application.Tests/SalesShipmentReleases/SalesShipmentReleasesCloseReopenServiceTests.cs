using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesCloseReopenServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesShipmentReleasesCloseService CloseService() => new(_db.Context);
    private SalesShipmentReleasesReopenService ReopenService() => new(_db.Context);

    private async Task<SalesShipmentRelease> SeedAsync(ReleaseStatus status)
    {
        var sr = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01", ReleasedQuantity = 100m, Status = status,
        };
        _db.Context.SalesShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private Task<SalesShipmentRelease> ReloadAsync(Guid key) =>
        _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    [Theory]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    public async Task Close_FromActivedOrPaused_SetsCompleted(ReleaseStatus status)
    {
        var sr = await SeedAsync(status);

        await CloseService().ExecuteAsync(sr.Key, "joao");

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Completed, saved.Status);
        Assert.Equal("joao", saved.UpdatedBy);
    }

    [Theory]
    [InlineData(ReleaseStatus.Pending)]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Completed)]
    public async Task Close_FromInvalidStatus_Throws(ReleaseStatus status)
    {
        var sr = await SeedAsync(status);

        await Assert.ThrowsAsync<NotFoundException>(() => CloseService().ExecuteAsync(sr.Key, "joao"));
    }

    [Fact]
    public async Task Reopen_FromCompleted_SetsActived()
    {
        var sr = await SeedAsync(ReleaseStatus.Completed);

        await ReopenService().ExecuteAsync(sr.Key, "joao");

        Assert.Equal(ReleaseStatus.Actived, (await ReloadAsync(sr.Key)).Status);
    }

    [Theory]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Cancelled)]
    public async Task Reopen_FromNonCompleted_Throws(ReleaseStatus status)
    {
        var sr = await SeedAsync(status);

        await Assert.ThrowsAsync<NotFoundException>(() => ReopenService().ExecuteAsync(sr.Key, "joao"));
    }
}
