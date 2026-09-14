using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsCreateServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task SeedPurchaseAsync(decimal quantity, DateTime date) =>
        _ctx.SeedStockAsync(StorageTransactionType.Purchase, quantity, date);

    [Fact]
    public async Task Create_stores_a_draft_with_snapshot_code_names_and_the_warehouse_as_partner()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));

        var r = await _ctx.CreateDraftAsync(950m, Today.AddHours(15));

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Draft, saved.Status);
        Assert.Equal(Today, saved.ReferenceDate);
        Assert.Equal(1_000m, saved.SystemBalance);
        Assert.Equal(-50m, saved.Difference);
        Assert.Equal(WarehouseReconciliationsTestContext.ThirdPartyWarehouse, saved.CardCode);
        Assert.Equal("Armazém Terceiro Ltda", saved.CardName);
        Assert.Equal("Armazém Terceiro", saved.WarehouseName);
        Assert.Equal("SOJA EM GRAOS", saved.ItemName);
        Assert.False(string.IsNullOrEmpty(saved.Code));
        Assert.Equal("tester", saved.CreatedBy);
    }

    [Fact]
    public async Task Snapshot_ignores_transactions_after_the_reference_date()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));
        await SeedPurchaseAsync(500m, Today.AddDays(-1));

        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));

        Assert.Equal(1_000m, (await _ctx.ReloadAsync(r.Key)).SystemBalance);
    }

    [Fact]
    public async Task Own_warehouse_is_refused()
    {
        await _ctx.SeedOwnWarehouseAsync();
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(
            reason.Key, 10m, warehouse: WarehouseReconciliationsTestContext.OwnWarehouse);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_OWN_WAREHOUSE", ex.Message);
    }

    [Fact]
    public async Task Negative_reported_balance_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, -1m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NEGATIVE_REPORTED_BALANCE", ex.Message);
    }

    [Fact]
    public async Task Future_reference_date_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m, Today.AddDays(1));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_FUTURE_DATE", ex.Message);
    }

    [Fact]
    public async Task Inactive_reason_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync(active: false);
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_INACTIVE", ex.Message);
    }

    [Fact]
    public async Task Missing_branch_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);
        r.BranchCode = null;

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REQUIRED_FIELDS", ex.Message);
    }

    [Theory]
    [InlineData(WarehouseReconciliationStatus.Draft)]
    [InlineData(WarehouseReconciliationStatus.InApproval)]
    public async Task A_second_open_reconciliation_for_the_same_warehouse_and_item_is_refused(
        WarehouseReconciliationStatus openStatus)
    {
        await _ctx.SeedWithStatusAsync(openStatus, Today.AddDays(-5));
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ALREADY_OPEN", ex.Message);
    }

    [Fact]
    public async Task Rejected_or_cancelled_do_not_block_a_new_one()
    {
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Rejected, Today.AddDays(-5));
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Cancelled, Today.AddDays(-5));

        var r = await _ctx.CreateDraftAsync(10m);

        Assert.Equal(WarehouseReconciliationStatus.Draft, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Reference_date_before_the_last_approved_is_refused()
    {
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, Today.AddDays(-5));
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m, Today.AddDays(-6));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DATE_BEFORE_LAST_APPROVED", ex.Message);
    }

    [Fact]
    public async Task Update_recomputes_the_difference()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(950m);
        var input = WarehouseReconciliationsTestContext.NewReconciliation(r.ReasonKey, 1_100m);

        await _ctx.Update().ExecuteAsync(r.Key, input, "editor");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(1_100m, saved.ReportedBalance);
        Assert.Equal(100m, saved.Difference);
        Assert.Equal("editor", saved.UpdatedBy);
    }

    [Fact]
    public async Task Update_outside_draft_is_refused()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.InApproval, Today);
        var input = WarehouseReconciliationsTestContext.NewReconciliation(r.ReasonKey, 1m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Update().ExecuteAsync(r.Key, input, "editor"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_DRAFT", ex.Message);
    }

    [Fact]
    public async Task Preview_reports_balance_ownership_open_and_last_approved()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, Today.AddDays(-20));
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, Today.AddDays(-10));

        var preview = await _ctx.Preview().ExecuteAsync(
            WarehouseReconciliationsTestContext.ThirdPartyWarehouse, WarehouseReconciliationsTestContext.Item, Today);

        Assert.Equal(1_000m, preview.SystemBalance);
        Assert.False(preview.IsOwnWarehouse);
        Assert.True(preview.HasOpenReconciliation);
        Assert.Equal(Today.AddDays(-20), preview.LastApprovedReferenceDate);
    }

    [Fact]
    public async Task Preview_flags_own_warehouse()
    {
        await _ctx.SeedOwnWarehouseAsync();

        var preview = await _ctx.Preview().ExecuteAsync(
            WarehouseReconciliationsTestContext.OwnWarehouse, WarehouseReconciliationsTestContext.Item, Today);

        Assert.True(preview.IsOwnWarehouse);
    }
}
