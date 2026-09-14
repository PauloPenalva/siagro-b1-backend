using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsApprovalFlowTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task SeedAsync(StorageTransactionType type, decimal quantity, DateTime date) =>
        _ctx.SeedStockAsync(type, quantity, date);

    [Fact]
    public async Task Send_moves_draft_to_in_approval_and_stamps_sender()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, saved.Status);
        Assert.Equal("sender", saved.SentBy);
        Assert.NotNull(saved.SentAt);
    }

    [Fact]
    public async Task Send_recomputes_a_stale_snapshot()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        // Romaneio lançado depois do rascunho, mas com data anterior à referência.
        await SeedAsync(StorageTransactionType.Purchase, 50m, Today.AddDays(-20));

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(1_050m, saved.SystemBalance);
        Assert.Equal(-150m, saved.Difference);
    }

    [Fact]
    public async Task Send_refuses_zero_difference()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(1_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.Draft, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Send_refuses_non_draft()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Rejected, Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_DRAFT", ex.Message);
    }

    [Fact]
    public async Task Withdraw_returns_to_draft_and_clears_sender()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        await _ctx.Withdraw().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Draft, saved.Status);
        Assert.Null(saved.SentBy);
    }

    [Fact]
    public async Task Reject_is_final_and_keeps_comments()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        await _ctx.Reject().ExecuteAsync(r.Key, "extrato ilegível", "approver");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Rejected, saved.Status);
        Assert.Equal("extrato ilegível", saved.ApprovalComments);
        Assert.Null(saved.StorageTransactionKey);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Withdraw().ExecuteAsync(r.Key, "sender"));
        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL", ex.Message);
    }

    [Fact]
    public async Task Approving_a_loss_generates_a_confirmed_warehouse_loss_transaction()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var referenceDate = Today.AddDays(-5);

        var r = await _ctx.CreateApprovedAsync(880m, referenceDate);

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Approved, saved.Status);
        Assert.Equal("approver", saved.ApprovedBy);
        Assert.Equal("ok", saved.ApprovalComments);
        Assert.NotNull(saved.StorageTransactionKey);

        var transaction = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == saved.StorageTransactionKey);
        Assert.Equal(StorageTransactionType.WarehouseLoss, transaction.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, transaction.TransactionStatus);
        Assert.Equal(TransactionCode.WarehouseReconciliation, transaction.TransactionOrigin);
        Assert.Equal(120m, transaction.NetWeight);
        Assert.Equal(120m, transaction.GrossWeight);
        Assert.Equal(decimal.Zero, transaction.AvaiableVolumeToAllocate);
        Assert.Equal(referenceDate, transaction.TransactionDate);
        Assert.Null(transaction.StorageAddressCode);
        Assert.Equal(WarehouseReconciliationsTestContext.ThirdPartyWarehouse, transaction.CardCode);
        Assert.Equal(WarehouseReconciliationsTestContext.ThirdPartyWarehouse, transaction.WarehouseCode);

        Assert.Equal(880m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Approving_a_gain_generates_a_warehouse_gain_transaction()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));

        var r = await _ctx.CreateApprovedAsync(1_030m, Today);

        var saved = await _ctx.ReloadAsync(r.Key);
        var transaction = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == saved.StorageTransactionKey);
        Assert.Equal(StorageTransactionType.WarehouseGain, transaction.TransactionType);
        Assert.Equal(30m, transaction.NetWeight);
        Assert.Equal(1_030m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Approval_recomputes_the_snapshot_before_generating()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await SeedAsync(StorageTransactionType.PurchaseReturn, 40m, Today.AddDays(-15));

        await _ctx.Approval().ExecuteAsync(r.Key, null, "approver");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(960m, saved.SystemBalance);
        Assert.Equal(-60m, saved.Difference);
    }

    [Fact]
    public async Task Approval_refuses_when_the_difference_became_zero()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await SeedAsync(StorageTransactionType.PurchaseReturn, 100m, Today.AddDays(-15));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
        Assert.False(await _ctx.Db.Context.StorageTransactions.AnyAsync(
            x => x.TransactionOrigin == TransactionCode.WarehouseReconciliation));
    }

    /// <summary>
    /// A perda apurada na data de referência não pode exceder o saldo ATUAL: 1.000 em estoque
    /// até a referência, 900 embarcados depois, perda de 300 deixaria o armazém em −200.
    /// </summary>
    [Fact]
    public async Task Approval_refuses_a_loss_bigger_than_the_current_balance()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(700m, Today.AddDays(-10));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await SeedAsync(StorageTransactionType.SalesShipment, 900m, Today.AddDays(-2));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_LOSS_EXCEEDS_BALANCE", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Approval_refuses_non_in_approval()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL", ex.Message);
    }
}
