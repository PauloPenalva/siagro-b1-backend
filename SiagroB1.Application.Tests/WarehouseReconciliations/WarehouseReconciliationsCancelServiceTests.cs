using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsCancelServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task SeedAsync(StorageTransactionType type, decimal quantity, DateTime date) =>
        _ctx.SeedStockAsync(type, quantity, date);

    [Fact]
    public async Task Cancelling_a_draft_has_no_stock_effect()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        await _ctx.Cancel().ExecuteAsync(r.Key, "digitado errado", "tester");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Cancelled, saved.Status);
        Assert.Equal("digitado errado", saved.CancellationReason);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.Equal(1_000m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Cancelling_an_approved_loss_cancels_its_transaction_and_restores_the_balance()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(880m, Today.AddDays(-5));
        Assert.Equal(880m, await _ctx.CurrentBalanceAsync());

        await _ctx.Cancel().ExecuteAsync(r.Key, "armazém corrigiu o extrato", "tester");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Cancelled, saved.Status);
        var transaction = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == saved.StorageTransactionKey);
        Assert.Equal(StorageTransactionsStatus.Cancelled, transaction.TransactionStatus);
        Assert.Equal(1_000m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Only_the_latest_approved_can_be_cancelled()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var older = await _ctx.CreateApprovedAsync(900m, Today.AddDays(-10));
        await _ctx.CreateApprovedAsync(950m, Today.AddDays(-5));

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(older.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_LATEST", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.Approved, (await _ctx.ReloadAsync(older.Key)).Status);
    }

    /// <summary>
    /// A sobra de 200 já foi embarcada: 1.200 no armazém, 1.150 saíram. Cancelar a sobra
    /// deixaria o armazém em −150.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_gain_that_was_already_shipped_is_refused()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var gain = await _ctx.CreateApprovedAsync(1_200m, Today.AddDays(-10));
        await SeedAsync(StorageTransactionType.SalesShipment, 1_150m, Today.AddDays(-1));

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(gain.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE", ex.Message);
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
