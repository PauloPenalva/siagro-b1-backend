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
        SalesContractAllocationOrigin origin = SalesContractAllocationOrigin.Billing,
        Guid? itemKey = null, bool owner = false) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = Guid.NewGuid(),
        SalesInvoiceItemKey = itemKey ?? Guid.NewGuid(),
        SalesShipmentReleaseKey = releaseKey,
        Volume = volume,
        Origin = origin,
        OwnsDeliveryDifference = owner,
    };

    private SalesInvoiceItem SeedItem(
        decimal quantity,
        SalesInvoiceDeliveryStatus deliveryStatus = SalesInvoiceDeliveryStatus.Open,
        decimal delivered = 0m, decimal loss = 0m)
    {
        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            DeliveredQuantity = delivered,
            QuantityLoss = loss,
            DeliveryStatus = deliveryStatus,
        };
        _db.Context.SalesInvoicesItems.Add(item);
        return item;
    }

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

    [Fact]
    public async Task Recalc_OpenItem_DeductsNominalQuantity()
    {
        // Entrega ainda não conferida: consome o volume nominal, mesmo que a balança do
        // cliente já tenha números lançados.
        var sr = await SeedReleaseAsync(released: 1000m);
        var item = SeedItem(100m, SalesInvoiceDeliveryStatus.Open, delivered: 90m);
        _db.Context.SalesContractsAllocations.Add(
            Alloc(sr.Key, 100m, itemKey: item.Key!.Value, owner: true));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(100m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_ClosedItemWithLoss_DeductsNetQuantity()
    {
        // Mesma regra do contrato: entrega encerrada consome o líquido (entregue − perda),
        // devolvendo a quebra ao saldo da liberação.
        var sr = await SeedReleaseAsync(released: 1000m);
        var item = SeedItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 95m, loss: 5m);
        _db.Context.SalesContractsAllocations.Add(
            Alloc(sr.Key, 100m, itemKey: item.Key!.Value, owner: true));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(90m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_ClosedItemSplitBetweenReleases_ShortageFallsOnOwnerReleaseOnly()
    {
        // Item dividido em duas liberações: a quebra não é rateada, sai inteira da
        // liberação da linha dona (mesma concentração do contrato).
        var owner = await SeedReleaseAsync(released: 1000m);
        var other = await SeedReleaseAsync(released: 1000m);
        var item = SeedItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 92m, loss: 2m);
        _db.Context.SalesContractsAllocations.AddRange(
            Alloc(owner.Key, 60m, itemKey: item.Key!.Value, owner: true),
            Alloc(other.Key, 40m, itemKey: item.Key!.Value));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(owner.Key);
        await Service().RecalculateAsync(other.Key);

        Assert.Equal(50m, await ShippedAsync(owner.Key)); // 60 − 10 de quebra
        Assert.Equal(40m, await ShippedAsync(other.Key)); // nominal
    }

    [Fact]
    public async Task Recalc_ClosedItemWithoutShortage_DeductsNominalQuantity()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        var item = SeedItem(100m, SalesInvoiceDeliveryStatus.Closed, delivered: 100m);
        _db.Context.SalesContractsAllocations.Add(
            Alloc(sr.Key, 100m, itemKey: item.Key!.Value, owner: true));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(100m, await ShippedAsync(sr.Key));
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
