using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsDistributeLossServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    [Fact]
    public async Task Distribution_replaces_the_previous_lines()
    {
        var a = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(39_000m);

        await _ctx.DistributeAsync(r.Key, (a, 1_000m));
        await _ctx.DistributeAsync(r.Key, (a, 600m), (b, 400m));

        var lines = await _ctx.Db.Context.WarehouseReconciliationReleases.AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == r.Key).OrderBy(x => x.Quantity).ToListAsync();
        Assert.Equal(new[] { 400m, 600m }, lines.Select(x => x.Quantity).ToArray());
    }

    [Fact]
    public async Task Distribution_outside_draft_is_refused()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.InApproval, Today);
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.DistributeAsync(r.Key, (release, 10m)));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_DRAFT", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Non_positive_quantity_is_refused(int quantity)
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.DistributeAsync(r.Key, (release, quantity)));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID", ex.Message);
    }

    [Fact]
    public async Task Quantity_that_rounds_to_zero_is_refused()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.DistributeAsync(r.Key, (release, 0.0004m)));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID", ex.Message);
    }

    [Fact]
    public async Task Duplicated_release_is_refused()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.DistributeAsync(r.Key, (release, 50m), (release, 50m)));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_gain()
    {
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(1_200m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ONLY_LOSS", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_distribution_that_does_not_close()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 60m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_paused_release()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), status: ReleaseStatus.Paused);
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASE_NOT_ELIGIBLE", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_line_above_the_current_release_balance()
    {
        var a = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseAsync(50m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (a, 50m), (b, 100m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASE_INSUFFICIENT_BALANCE", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_standard_release_of_a_finished_contract()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), contractStatus: ContractStatus.Finished);
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_CONTRACT_FINISHED", ex.Message);
    }

    [Fact]
    public async Task Send_accepts_a_closed_distribution_across_two_releases()
    {
        var a = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(39_000m);
        await _ctx.DistributeAsync(r.Key, (a, 600m), (b, 400m));

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
    }
}
