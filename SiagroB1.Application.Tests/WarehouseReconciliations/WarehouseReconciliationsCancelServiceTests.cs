using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsCancelServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task<ShipmentRelease> SeedReleaseAsync(decimal q, DateTime d) => _ctx.SeedReleaseAsync(q, d);

    [Fact]
    public async Task Cancelling_a_draft_has_no_effect()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "digitado errado", "tester");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Cancelled, saved.Status);
        Assert.Equal("digitado errado", saved.CancellationReason);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.Equal(1_000m, (await _ctx.ReloadReleaseAsync(release.Key)).AvailableQuantity);
    }

    [Fact]
    public async Task Cancelling_an_approved_standard_loss_restores_the_releases_and_removes_the_allocation()
    {
        var a = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(39_000m, Today.AddDays(-2), (a, 600m), (b, 400m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "armazém corrigiu o extrato", "tester");

        Assert.Equal(WarehouseReconciliationStatus.Cancelled, (await _ctx.ReloadAsync(r.Key)).Status);
        Assert.Equal(20_000m, (await _ctx.ReloadReleaseAsync(a.Key)).AvailableQuantity);
        Assert.Equal(20_000m, (await _ctx.ReloadReleaseAsync(b.Key)).AvailableQuantity);
        Assert.False(await _ctx.Db.Context.PurchaseContractsAllocations.AnyAsync());

        var generated = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .Where(x => x.TransactionOrigin == TransactionCode.WarehouseReconciliation).ToListAsync();
        Assert.Equal(4, generated.Count);
        Assert.All(generated, t => Assert.Equal(StorageTransactionsStatus.Cancelled, t.TransactionStatus));

        var contract = await _ctx.Db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == a.PurchaseContractKey);
        Assert.Equal(0m, contract.AllocatedVolume);
    }

    /// <summary>Duas linhas no mesmo contrato: cancelar precisa zerar o AllocatedVolume, não só a última linha (fix round 1).</summary>
    [Fact]
    public async Task Cancelling_an_approved_standard_loss_on_the_same_contract_zeroes_the_allocation()
    {
        var a = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseOnSameContractAsync(a, 20_000m, Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(39_000m, Today.AddDays(-2), (a, 600m), (b, 400m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "armazém corrigiu o extrato", "tester");

        var contract = await _ctx.Db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == a.PurchaseContractKey);
        Assert.Equal(0m, contract.AllocatedVolume);
        Assert.Equal(20_000m, (await _ctx.ReloadReleaseAsync(a.Key)).AvailableQuantity);
        Assert.Equal(20_000m, (await _ctx.ReloadReleaseAsync(b.Key)).AvailableQuantity);
    }

    [Fact]
    public async Task Cancelling_an_approved_sales_return_loss_restores_release_and_warehouse()
    {
        await _ctx.SeedStockAsync(StorageTransactionType.SalesShipmentReturn, 29_400m, Today.AddDays(-30));
        var release = await _ctx.SeedReleaseAsync(29_400m, Today.AddDays(-30), origin: ReleaseOrigin.SalesReturn);
        var r = await _ctx.CreateApprovedAsync(29_000m, Today, (release, 400m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "motivo", "tester");

        Assert.Equal(29_400m, (await _ctx.ReloadReleaseAsync(release.Key)).AvailableQuantity);
        Assert.Equal(29_400m, await _ctx.CurrentBalanceAsync());
    }

    /// <summary>Conferência aprovada ANTES da revisão de 17/09: sem linhas, cancela pelo romaneio único.</summary>
    [Fact]
    public async Task Cancelling_a_legacy_approved_reconciliation_cancels_its_single_transaction()
    {
        await _ctx.SeedStockAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var loss = await _ctx.SeedStockAsync(StorageTransactionType.WarehouseLoss, 120m, Today.AddDays(-5));
        loss.TransactionOrigin = TransactionCode.WarehouseReconciliation;
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, Today.AddDays(-5), -120m);
        r.StorageTransactionKey = loss.Key;
        await _ctx.Db.Context.SaveChangesAsync();

        await _ctx.Cancel().ExecuteAsync(r.Key, "legado", "tester");

        Assert.Equal(StorageTransactionsStatus.Cancelled,
            (await _ctx.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == loss.Key)).TransactionStatus);
        Assert.Equal(1_000m, await _ctx.CurrentBalanceAsync());
    }

    /// <summary>Legado (sem linhas de distribuição): a SOBRA aprovada tirava o guard de saldo negativo
    /// nesta revisão. Refix round 2 — pré-revisão de 17/09, cancelar uma SOBRA não pode deixar o
    /// armazém negativo hoje.</summary>
    [Fact]
    public async Task Cancelling_a_legacy_approved_gain_refuses_when_it_would_leave_the_warehouse_negative()
    {
        await _ctx.SeedStockAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var gain = await _ctx.SeedStockAsync(StorageTransactionType.WarehouseGain, 200m, Today.AddDays(-5));
        gain.TransactionOrigin = TransactionCode.WarehouseReconciliation;
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, Today.AddDays(-5), 200m);
        r.StorageTransactionKey = gain.Key;
        await _ctx.Db.Context.SaveChangesAsync();
        await _ctx.SeedStockAsync(StorageTransactionType.SalesShipment, 1_150m, Today.AddDays(-2));

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(r.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.Approved, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Only_the_latest_approved_can_be_cancelled()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var older = await _ctx.CreateApprovedAsync(900m, Today.AddDays(-10), (release, 100m));
        await _ctx.CreateApprovedAsync(850m, Today.AddDays(-5), (release, 50m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(older.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_LATEST", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.Approved, (await _ctx.ReloadAsync(older.Key)).Status);
    }

    [Theory]
    [InlineData(WarehouseReconciliationStatus.InApproval)]
    [InlineData(WarehouseReconciliationStatus.Rejected)]
    [InlineData(WarehouseReconciliationStatus.Cancelled)]
    public async Task Other_statuses_cannot_be_cancelled(WarehouseReconciliationStatus status)
    {
        var r = await _ctx.SeedWithStatusAsync(status, Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(r.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_CANNOT_CANCEL", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Cancellation_reason_is_required(string? reason)
    {
        var r = await _ctx.CreateDraftAsync(10m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(r.Key, reason, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_CANCELLATION_REASON_REQUIRED", ex.Message);
    }
}
