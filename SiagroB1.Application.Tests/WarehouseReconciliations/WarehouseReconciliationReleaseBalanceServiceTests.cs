using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationReleaseBalanceServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;
    private const string Wh = WarehouseReconciliationsTestContext.ThirdPartyWarehouse;
    private const string Item = WarehouseReconciliationsTestContext.Item;

    [Fact]
    public async Task Balance_today_is_released_minus_shipped()
    {
        var release = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 5_000m, Today.AddDays(-5));

        var rows = await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today);

        var row = Assert.Single(rows);
        Assert.Equal(15_000m, row.BalanceAtReferenceDate);
        Assert.Equal(15_000m, row.CurrentBalance);
        Assert.True(row.CanReceiveLoss);
        Assert.Equal(WarehouseReconciliationsTestContext.Producer, row.CardCode);
    }

    [Fact]
    public async Task Shipments_after_the_reference_date_are_added_back()
    {
        var release = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 3_000m, Today.AddDays(-20));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 4_000m, Today.AddDays(-2));

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today.AddDays(-10)));

        Assert.Equal(17_000m, row.BalanceAtReferenceDate);
        Assert.Equal(13_000m, row.CurrentBalance);
    }

    [Fact]
    public async Task Release_issued_after_the_reference_date_is_left_out()
    {
        await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseAsync(9_000m, Today.AddDays(-1));

        var rows = await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today.AddDays(-5));

        Assert.Equal(20_000m, Assert.Single(rows).BalanceAtReferenceDate);
    }

    [Fact]
    public async Task Paused_counts_but_cannot_receive_loss()
    {
        await _ctx.SeedReleaseAsync(8_000m, Today.AddDays(-30), status: ReleaseStatus.Paused);

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));

        Assert.Equal(8_000m, row.BalanceAtReferenceDate);
        Assert.False(row.CanReceiveLoss);
    }

    [Fact]
    public async Task Pending_and_cancelled_releases_are_left_out()
    {
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), status: ReleaseStatus.Pending);
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), status: ReleaseStatus.Cancelled);

        Assert.Empty(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));
    }

    [Fact]
    public async Task Completed_after_the_reference_date_still_counts_at_that_date()
    {
        var release = await _ctx.SeedReleaseAsync(2_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 2_000m, Today.AddDays(-1));
        var reloaded = await _ctx.ReloadReleaseAsync(release.Key);
        if (reloaded.Status != ReleaseStatus.Completed)
        {
            // O recálculo não finaliza sozinho neste código: simula a finalização.
            var tracked = await _ctx.Db.Context.ShipmentReleases.FindAsync(release.Key);
            tracked!.Status = ReleaseStatus.Completed;
            await _ctx.Db.Context.SaveChangesAsync();
        }

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today.AddDays(-5)));

        Assert.Equal(2_000m, row.BalanceAtReferenceDate);
        Assert.Equal(0m, row.CurrentBalance);
        Assert.False(row.CanReceiveLoss);
    }

    [Fact]
    public async Task Completed_release_with_a_finalized_leftover_is_left_out()
    {
        // ShipmentReleasesCloseService só troca o Status; não zera Released/Shipped. O saldo que
        // sobra ali já foi dado por encerrado e não pode inflar o saldo do sistema (fix round 2).
        await _ctx.SeedReleaseAsync(3_000m, Today.AddDays(-30), status: ReleaseStatus.Completed);

        Assert.Empty(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));
    }

    [Fact]
    public async Task Standard_release_of_a_finished_contract_counts_but_cannot_receive_loss()
    {
        await _ctx.SeedReleaseAsync(8_000m, Today.AddDays(-30), contractStatus: ContractStatus.Finished);

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));

        Assert.Equal(8_000m, row.BalanceAtReferenceDate);
        Assert.False(row.CanReceiveLoss);
    }

    [Fact]
    public async Task Sales_return_release_is_consumed_by_sales_shipment_and_loss()
    {
        var release = await _ctx.SeedReleaseAsync(29_400m, Today.AddDays(-30), origin: ReleaseOrigin.SalesReturn);
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.WarehouseLoss, 400m, Today.AddDays(-3));

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));

        Assert.Equal(29_000m, row.BalanceAtReferenceDate);
    }

    [Fact]
    public async Task Other_warehouse_or_item_is_ignored()
    {
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), warehouse: "OUTRO");

        Assert.Empty(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));
    }
}
