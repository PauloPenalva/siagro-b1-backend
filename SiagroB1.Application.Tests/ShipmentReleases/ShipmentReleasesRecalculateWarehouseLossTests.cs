using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// GAC-1164 §9: a Perda(13) gerada pela Conferência de Saldo consome a liberação quando a liberação
/// não tem perna de compra (transferência / devolução de venda). Na Standard quem consome é a
/// Compra(8) do par, e a Perda não pode contar de novo.
/// </summary>
public class ShipmentReleasesRecalculateWarehouseLossTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<ShipmentRelease> SeedReleaseAsync(ReleaseOrigin origin)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "ARM",
            Status = ContractStatus.Approved, TotalVolume = 10_000m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, DeliveryLocationCode = "ARM",
            ReleasedQuantity = 1_000m, Status = ReleaseStatus.Actived, Origin = origin,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();
        return release;
    }

    private async Task SeedTransactionAsync(Guid releaseKey, StorageTransactionType type, decimal qty)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = Guid.NewGuid().ToString("N")[..8], CardCode = "ARM", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM", TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed, GrossWeight = qty, NetWeight = qty,
            ShipmentReleaseKey = releaseKey,
        });
        await _db.Context.SaveChangesAsync();
    }

    [Theory]
    [InlineData(ReleaseOrigin.OwnershipTransfer)]
    [InlineData(ReleaseOrigin.SalesReturn)]
    public async Task Loss_consumes_a_release_without_purchase_leg(ReleaseOrigin origin)
    {
        var release = await SeedReleaseAsync(origin);
        await SeedTransactionAsync(release.Key, StorageTransactionType.SalesShipment, 300m);
        await SeedTransactionAsync(release.Key, StorageTransactionType.WarehouseLoss, 50m);

        var shipped = await new ShipmentReleasesRecalculateShippedService(_db.Context)
            .CalculateShippedAsync(release.Key, origin);

        Assert.Equal(350m, shipped);
    }

    [Fact]
    public async Task Loss_does_not_count_twice_on_a_standard_release()
    {
        var release = await SeedReleaseAsync(ReleaseOrigin.Standard);
        await SeedTransactionAsync(release.Key, StorageTransactionType.Purchase, 50m);
        await SeedTransactionAsync(release.Key, StorageTransactionType.WarehouseLoss, 50m);

        var shipped = await new ShipmentReleasesRecalculateShippedService(_db.Context)
            .CalculateShippedAsync(release.Key, ReleaseOrigin.Standard);

        Assert.Equal(50m, shipped);
    }

    [Fact]
    public void Warehouse_loss_affects_shipped_quantity()
    {
        Assert.True(ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(StorageTransactionType.WarehouseLoss));
        Assert.False(ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(StorageTransactionType.WarehouseGain));
    }

    [Theory]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.Purchase, 1)]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.PurchaseReturn, -1)]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.WarehouseLoss, 0)]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.SalesShipment, 0)]
    [InlineData(ReleaseOrigin.SalesReturn, StorageTransactionType.SalesShipment, 1)]
    [InlineData(ReleaseOrigin.SalesReturn, StorageTransactionType.WarehouseLoss, 1)]
    [InlineData(ReleaseOrigin.SalesReturn, StorageTransactionType.SalesShipmentReturn, -1)]
    [InlineData(ReleaseOrigin.OwnershipTransfer, StorageTransactionType.Purchase, 0)]
    public void Shipped_sign_matches_the_recalculation(ReleaseOrigin origin, StorageTransactionType type, int expected)
    {
        Assert.Equal(expected, ShipmentReleasesRecalculateShippedService.ShippedSign(origin, type));
    }

    [Fact]
    public async Task Movement_guard_refuses_a_loss_on_a_paused_release()
    {
        var release = await SeedReleaseAsync(ReleaseOrigin.SalesReturn);
        release.Status = ReleaseStatus.Paused;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            new ShipmentReleaseMovementGuardService(_db.Context).EnsureCanShipAsync(new StorageTransaction
            {
                CardCode = "ARM", ItemCode = "SOJA", UnitOfMeasureCode = "KG", WarehouseCode = "ARM",
                TransactionType = StorageTransactionType.WarehouseLoss, ShipmentReleaseKey = release.Key,
            }));

        Assert.Contains("pausada", ex.Message);
    }
}
