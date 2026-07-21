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

    private StorageTransaction Tx(Guid releaseKey, StorageTransactionType type, decimal net,
        StorageTransactionsStatus status = StorageTransactionsStatus.Invoiced) => new()
    {
        Key = Guid.NewGuid(),
        Code = "ST",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TransactionType = type,
        TransactionStatus = status,
        NetWeight = net,
        SalesShipmentReleaseKey = releaseKey,
    };

    private async Task<decimal> ShippedAsync(Guid key) =>
        (await _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key)).ShippedQuantity;

    [Fact]
    public async Task Recalc_SumsInvoicedSalesShipmentsNetWeight()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 400m),
            Tx(sr.Key, StorageTransactionType.SalesShipment, 150m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(550m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresReturnedAndCancelledAndConfirmed()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 400m),                                        // Invoiced conta
            Tx(sr.Key, StorageTransactionType.SalesShipment, 300m, StorageTransactionsStatus.Returned),   // devolvido restaura
            Tx(sr.Key, StorageTransactionType.SalesShipment, 200m, StorageTransactionsStatus.Cancelled),  // cancelado
            Tx(sr.Key, StorageTransactionType.SalesShipment, 100m, StorageTransactionsStatus.Confirmed)); // ainda não faturado
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(400m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresPurchaseTypes()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 500m),
            Tx(sr.Key, StorageTransactionType.PurchaseReturn, 200m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresOtherReleases()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        var other = Guid.NewGuid();
        _db.Context.StorageTransactions.AddRange(
            Tx(other, StorageTransactionType.SalesShipment, 70m),
            Tx(sr.Key, StorageTransactionType.SalesShipment, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_NoTransactions_SetsZero()
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
