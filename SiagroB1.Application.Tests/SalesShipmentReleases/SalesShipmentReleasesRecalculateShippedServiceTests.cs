using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesRecalculateShippedServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesShipmentReleasesRecalculateShippedService Service() => new(_db.Context);

    private async Task<SalesShipmentRelease> SeedReleaseAsync(decimal released)
    {
        var sr = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(),
            SalesContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = released,
            ShippedQuantity = 999m, // valor errado, deve ser recalculado
            Status = ReleaseStatus.Actived,
        };
        _db.Context.SalesShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private SalesContractAllocation Alloc(Guid? releaseKey, decimal volume,
        SalesContractAllocationOrigin origin = SalesContractAllocationOrigin.Billing) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = Guid.NewGuid(),
        SalesInvoiceItemKey = Guid.NewGuid(),
        SalesShipmentReleaseKey = releaseKey,
        Volume = volume,
        Origin = origin,
    };

    private async Task<decimal> ShippedAsync(Guid key) =>
        (await _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key)).ShippedQuantity;

    [Fact]
    public async Task Recalc_SumsSignedAllocationVolumes()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.SalesContractsAllocations.AddRange(
            Alloc(sr.Key, 400m),
            Alloc(sr.Key, 150m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(550m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_ReturnRowsRestoreBalance()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.SalesContractsAllocations.AddRange(
            Alloc(sr.Key, 400m),
            Alloc(sr.Key, -300m, SalesContractAllocationOrigin.Return)); // devolução devolve saldo
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(100m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_ReallocationMovesBalanceBetweenReleases()
    {
        var origin = await SeedReleaseAsync(released: 1000m);
        var target = await SeedReleaseAsync(released: 500m);
        _db.Context.SalesContractsAllocations.AddRange(
            Alloc(origin.Key, 400m),                                              // faturamento
            Alloc(origin.Key, -100m, SalesContractAllocationOrigin.Reallocation), // devolve à origem
            Alloc(target.Key, 100m, SalesContractAllocationOrigin.Reallocation)); // consome destino
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(origin.Key);
        await Service().RecalculateAsync(target.Key);

        Assert.Equal(300m, await ShippedAsync(origin.Key));
        Assert.Equal(100m, await ShippedAsync(target.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresOtherReleasesAndLegacyRowsWithoutRelease()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.SalesContractsAllocations.AddRange(
            Alloc(Guid.NewGuid(), 70m),  // outra liberação
            Alloc(null, 50m),            // fluxo legado, sem liberação
            Alloc(sr.Key, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_NoAllocations_SetsZero()
    {
        var sr = await SeedReleaseAsync(released: 1000m);

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }

    [Theory]
    [InlineData(StorageTransactionType.SalesShipment, true)]
    [InlineData(StorageTransactionType.SalesShipmentReturn, false)]
    [InlineData(StorageTransactionType.Purchase, false)]
    [InlineData(StorageTransactionType.PurchaseReturn, false)]
    [InlineData(StorageTransactionType.Transfer, false)]
    public void AffectsShippedQuantity_MatchesRule(StorageTransactionType type, bool expected)
    {
        Assert.Equal(expected, SalesShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(type));
    }
}
