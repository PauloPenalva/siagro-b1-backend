using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1213: o romaneio de quebra de trajeto (tipo 18) só registra a perda — não pode mexer em
/// nenhum saldo (armazém, lote, liberação, alocação de contrato).
/// </summary>
public class StorageTransactionTransitLossTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly Guid _loadKey = Guid.NewGuid();

    private ShipmentReleasesRecalculateShippedService Recalc() => new(_db.Context);

    private StorageTransactionsCreateService Create() => new(
        _db, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new() { ["C0001"] = "Cliente" }),
        new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeWarehouseService(new() { ["ARM01"] = "Armazém 01" }),
        Recalc(), new ShipmentReleaseMovementGuardService(_db.Context),
        NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService Confirm() => new(
        _db, new FakeStringLocalizer<Resource>(), Recalc(),
        new ShipmentReleaseMovementGuardService(_db.Context),
        NullLogger<StorageTransactionsConfirmedService>.Instance);

    private StorageTransactionsCancelService Cancel() => new(_db, Recalc());

    private StorageTransactionsReverseService Reverse() =>
        new(_db, null!, Recalc(), new FakeStringLocalizer<Resource>());

    private async Task<(Guid ReleaseKey, ReleaseOrigin Origin)> SeedBalancesAsync()
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "ARM01",
            ReleasedQuantity = 30_000m,
        };
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "RM000001", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM01", BranchCode = "01",
            GrossWeight = 50_000m, NetWeight = 50_000m,
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentReleaseKey = release.Key,
        });
        await _db.SaveChangesAsync();
        return (release.Key, release.Origin);
    }

    private StorageTransaction NewTransitLoss() => new()
    {
        TransactionType = StorageTransactionType.TransitLoss,
        TransactionStatus = StorageTransactionsStatus.Pending,
        TransactionDate = DateTime.Now.Date,
        BranchCode = "01",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "ARM01",
        CardCode = "C0001",
        GrossWeight = 15_000m,
        NetWeight = 15_000m,
        TransitLossReason = TransitLossReason.Accident,
        RefusedFromShipmentLoadKey = _loadKey,
    };

    private async Task<StorageTransaction> CreateAndConfirmAsync()
    {
        var entry = NewTransitLoss();
        await Create().ExecuteAsync(entry, "tester", TransactionCode.ShipmentLoad, CommitMode.Deferred);
        await _db.SaveChangesAsync();
        await Confirm().ExecuteAsync(entry, "tester", CommitMode.Deferred);
        await _db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task Confirming_only_marks_it_confirmed_without_discounts()
    {
        await SeedBalancesAsync();
        var entry = NewTransitLoss();
        entry.NetWeight = 0m; // o confirmar é quem iguala ao bruto

        await Create().ExecuteAsync(entry, "tester", TransactionCode.ShipmentLoad, CommitMode.Deferred);
        await _db.SaveChangesAsync();
        await Confirm().ExecuteAsync(entry, "tester", CommitMode.Deferred);
        await _db.SaveChangesAsync();

        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Equal(15_000m, entry.NetWeight);
        Assert.Equal(entry.GrossWeight, entry.NetWeight);
        Assert.Equal(0m, entry.AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task It_does_not_move_the_warehouse_balance()
    {
        await SeedBalancesAsync();
        var before = await StorageTransactionsWarehouseBalanceService.CalculateAsync(_db.Context, "ARM01", "SOJA");

        await CreateAndConfirmAsync();

        var after = await StorageTransactionsWarehouseBalanceService.CalculateAsync(_db.Context, "ARM01", "SOJA");
        Assert.Equal(50_000m, before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task It_does_not_change_the_shipped_quantity_of_a_release()
    {
        var (releaseKey, origin) = await SeedBalancesAsync();
        var before = await Recalc().CalculateShippedAsync(releaseKey, origin);

        await CreateAndConfirmAsync();

        var after = await Recalc().CalculateShippedAsync(releaseKey, origin);
        Assert.Equal(50_000m, before);
        Assert.Equal(before, after);
        Assert.False(ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(
            StorageTransactionType.TransitLoss));
    }

    [Fact]
    public async Task It_creates_no_contract_allocation()
    {
        await SeedBalancesAsync();

        await CreateAndConfirmAsync();

        Assert.Empty(_db.Context.PurchaseContractsAllocations);
        Assert.Empty(_db.Context.SalesContractsAllocations);
    }

    [Fact]
    public async Task It_cannot_be_cancelled_from_the_shipments_screen()
    {
        var entry = await CreateAndConfirmAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Cancel().ExecuteAsync(entry.Key, "tester", TransactionCode.ShipmentLoad));

        Assert.Contains("recusa da carga", error.Message);
    }

    [Fact]
    public async Task It_cannot_be_reversed_from_the_shipments_screen()
    {
        var entry = await CreateAndConfirmAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Reverse().ExecuteAsync(entry.Key, "tester", TransactionCode.ShipmentLoad));

        Assert.Contains("recusa da carga", error.Message);
    }
}
