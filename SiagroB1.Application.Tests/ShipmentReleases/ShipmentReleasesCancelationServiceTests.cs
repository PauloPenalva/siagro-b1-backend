using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesCancelationServiceTests
{
    private const string Reason = "troca de armazém";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesCancelationService Service() =>
        new(_db.Context,
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            NullLogger<ShipmentReleasesCancelationService>.Instance);

    private async Task<ShipmentRelease> SeedReleaseAsync(
        ReleaseStatus status = ReleaseStatus.Actived,
        decimal released = 100m,
        ContractStatus contractStatus = ContractStatus.Approved)
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            TotalVolume = released, Status = contractStatus,
        };
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = pc.Key,
            DeliveryLocationCode = "01", ReleasedQuantity = released, Status = status,
        };
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private async Task AddTransactionAsync(
        Guid releaseKey, StorageTransactionType type, decimal netWeight, string code = "ST-001",
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "01",
            TransactionType = type, TransactionStatus = status,
            NetWeight = netWeight, ShipmentReleaseKey = releaseKey,
        });
        await _db.Context.SaveChangesAsync();
    }

    private Task<ShipmentRelease> ReloadAsync(Guid key) =>
        _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    // ---------- caso de uso principal: troca de armazém ----------

    [Fact]
    public async Task Cancel_ActivedWithPurchase_Succeeds_AndConsumesOnlyShipped()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 300m);

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Cancelled, saved.Status);
        Assert.Equal(300m, saved.ShippedQuantity);
        Assert.Equal(300m, saved.ConsumedQuantity);
        Assert.Equal(0m, saved.AvailableQuantity);
    }

    [Fact]
    public async Task Cancel_PausedWithPurchase_Succeeds()
    {
        var sr = await SeedReleaseAsync(status: ReleaseStatus.Paused, released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 300m);

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        Assert.Equal(ReleaseStatus.Cancelled, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Cancel_FreezesShippedQuantityFromCurrentTransactions()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 400m, "ST-A");
        await AddTransactionAsync(sr.Key, StorageTransactionType.PurchaseReturn, 150m, "ST-B");

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        // 400 embarcados − 150 devolvidos
        Assert.Equal(250m, (await ReloadAsync(sr.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Cancel_StampsAuditFieldsAndReason()
    {
        var sr = await SeedReleaseAsync();

        await Service().ExecuteAsync(sr.Key, "maria", "  troca de armazém  ");

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal("troca de armazém", saved.CancellationReason);
        Assert.Equal("maria", saved.CanceledBy);
        Assert.Equal("maria", saved.UpdatedBy);
        Assert.NotNull(saved.CanceledAt);
    }

    // ---------- romaneios vinculados não bloqueiam ----------

    [Fact]
    public async Task Cancel_PurchaseTransactions_CountAsConsumed()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 300m, "ST-777");

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(300m, saved.ShippedQuantity);
        Assert.Equal(300m, saved.ConsumedQuantity);
    }

    [Fact]
    public async Task Cancel_SalesShipmentLinked_DoesNotCountAsConsumed()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.SalesShipment, 300m, "ST-888");

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(0m, saved.ShippedQuantity);
        Assert.Equal(0m, saved.ConsumedQuantity);
    }

    [Fact]
    public async Task Cancel_MixedPurchaseAndSales_ConsumesOnlyPurchase()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 300m, "ST-A");
        await AddTransactionAsync(sr.Key, StorageTransactionType.SalesShipment, 200m, "ST-B");

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        Assert.Equal(300m, (await ReloadAsync(sr.Key)).ConsumedQuantity);
    }

    [Fact]
    public async Task Cancel_WithCancelledPurchaseTransaction_Succeeds()
    {
        var sr = await SeedReleaseAsync();
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 50m, "ST-999",
            StorageTransactionsStatus.Cancelled);

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        Assert.Equal(ReleaseStatus.Cancelled, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Cancel_WithZeroBalance_ThrowsSuggestingFinalize()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));

        Assert.Contains("Finalizar", ex.Message);
        Assert.Equal(ReleaseStatus.Actived, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Cancel_WithNegativeBalance_Throws()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 1200m);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));

        Assert.Equal(ReleaseStatus.Actived, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Cancel_RefusedForZeroBalance_PersistsNothing()
    {
        // ShippedQuantity persistido está desatualizado (0) e os romaneios zeram o saldo.
        // A recusa não pode gravar nada — nem o recálculo do campo derivado.
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 1000m);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));

        var stored = await ReloadAsync(sr.Key);
        Assert.Equal(0m, stored.ShippedQuantity);
        Assert.Equal(ReleaseStatus.Actived, stored.Status);
        Assert.Null(stored.CancellationReason);
        Assert.Null(stored.CanceledAt);
    }

    [Fact]
    public async Task Cancel_BalanceZeroOnlyAfterRecalc_Throws()
    {
        // ShippedQuantity persistido está desatualizado; o recálculo revela saldo zero
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 600m, "ST-A");
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 400m, "ST-B");

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));
    }

    [Fact]
    public async Task Cancel_WithRemainingBalance_StillSucceeds()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 999.999m);

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        Assert.Equal(ReleaseStatus.Cancelled, (await ReloadAsync(sr.Key)).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Cancel_WithoutReason_Throws(string? reason)
    {
        var sr = await SeedReleaseAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", reason!));
    }

    [Theory]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Completed)]
    public async Task Cancel_FromTerminalStatus_Throws(ReleaseStatus status)
    {
        var sr = await SeedReleaseAsync(status: status);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));
    }

    [Fact]
    public async Task Cancel_FinishedContract_Throws()
    {
        var sr = await SeedReleaseAsync(contractStatus: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(sr.Key, "maria", Reason));
    }

    // ---------- comportamento preservado ----------

    [Fact]
    public async Task Cancel_NoTransactions_Cancels_AndConsumesNothing()
    {
        var sr = await SeedReleaseAsync();

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Cancelled, saved.Status);
        Assert.Equal(0m, saved.ConsumedQuantity);
    }

    [Fact]
    public async Task Cancel_FromPending_Cancels()
    {
        var sr = await SeedReleaseAsync(status: ReleaseStatus.Pending);

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        Assert.Equal(ReleaseStatus.Cancelled, (await ReloadAsync(sr.Key)).Status);
    }
}
