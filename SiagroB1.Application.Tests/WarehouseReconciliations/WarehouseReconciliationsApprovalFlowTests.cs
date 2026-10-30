using SiagroB1.Infra.Enums;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsApprovalFlowTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task SeedAsync(StorageTransactionType type, decimal quantity, DateTime date) =>
        _ctx.SeedStockAsync(type, quantity, date);

    private Task<ShipmentRelease> SeedReleaseAsync(decimal q, DateTime d) => _ctx.SeedReleaseAsync(q, d);

    [Fact]
    public async Task Send_moves_draft_to_in_approval_and_stamps_sender()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, saved.Status);
        Assert.Equal("sender", saved.SentBy);
        Assert.NotNull(saved.SentAt);
    }

    [Fact]
    public async Task Send_recomputes_a_stale_snapshot()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        // Nova liberação emitida depois do rascunho, com data anterior à referência.
        await _ctx.SeedReleaseAsync(50m, Today.AddDays(-20));
        await _ctx.DistributeAsync(r.Key, (release, 150m));

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(1_050m, saved.SystemBalance);
        Assert.Equal(-150m, saved.Difference);
    }

    [Fact]
    public async Task Send_refuses_zero_difference()
    {
        await SeedReleaseAsync(1_000m, Today.AddDays(-30));
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
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        await _ctx.Withdraw().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Draft, saved.Status);
        Assert.Null(saved.SentBy);
    }

    [Fact]
    public async Task Reject_is_final_and_keeps_comments()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        await _ctx.Reject().ExecuteAsync(r.Key, "extrato ilegível", "approver");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Rejected, saved.Status);
        Assert.Equal("extrato ilegível", saved.ApprovalComments);
        Assert.Null(saved.StorageTransactionKey);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Withdraw().ExecuteAsync(r.Key, "sender"));
        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL", ex.Message);
    }

    /// <summary>Cenário do usuário (17/09): 2 contratos de 20.000, quebra de 1.000 → 39.000 a embarcar.</summary>
    [Fact]
    public async Task Approving_a_loss_on_standard_releases_generates_purchase_and_loss_and_consumes_each_release()
    {
        var a = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var referenceDate = Today.AddDays(-2);

        var r = await _ctx.CreateApprovedAsync(39_000m, referenceDate, (a, 600m), (b, 400m));

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Approved, saved.Status);
        Assert.Equal("approver", saved.ApprovedBy);
        Assert.Null(saved.StorageTransactionKey);

        Assert.Equal(19_400m, (await _ctx.ReloadReleaseAsync(a.Key)).AvailableQuantity);
        Assert.Equal(19_600m, (await _ctx.ReloadReleaseAsync(b.Key)).AvailableQuantity);

        var lines = await _ctx.Db.Context.WarehouseReconciliationReleases.AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == r.Key).ToListAsync();
        Assert.All(lines, l => Assert.NotNull(l.PurchaseStorageTransactionKey));
        Assert.All(lines, l => Assert.NotNull(l.LossStorageTransactionKey));

        var lineA = lines.Single(l => l.ShipmentReleaseKey == a.Key);
        var purchase = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == lineA.PurchaseStorageTransactionKey);
        var loss = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == lineA.LossStorageTransactionKey);

        Assert.Equal(StorageTransactionType.Purchase, purchase.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, purchase.TransactionStatus);
        Assert.Equal(TransactionCode.WarehouseReconciliation, purchase.TransactionOrigin);
        Assert.Equal(600m, purchase.NetWeight);
        Assert.Equal(referenceDate, purchase.TransactionDate);
        Assert.Equal(WarehouseReconciliationsTestContext.Producer, purchase.CardCode);

        Assert.Equal(StorageTransactionType.WarehouseLoss, loss.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, loss.TransactionStatus);
        Assert.Equal(TransactionCode.WarehouseReconciliation, loss.TransactionOrigin);
        Assert.Equal(600m, loss.NetWeight);
        Assert.Equal(decimal.Zero, loss.AvaiableVolumeToAllocate);
        Assert.Equal(a.Key, loss.ShipmentReleaseKey);
        Assert.Null(loss.StorageAddressCode);
        Assert.Equal(referenceDate, loss.TransactionDate);

        // A perda é custo da empresa: o contrato do produtor conta como entregue.
        var allocation = await _ctx.Db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == purchase.Key);
        Assert.Equal(a.PurchaseContractKey, allocation.PurchaseContractKey);
        Assert.Equal(600m, allocation.Volume);

        // Compra + Perda se anulam no saldo por romaneios.
        Assert.Equal(0m, await _ctx.CurrentBalanceAsync());
    }

    /// <summary>
    /// Duas linhas de distribuição na MESMA conferência caem no mesmo contrato: a segunda alocação
    /// não pode perder a primeira, ainda não persistida quando ela roda (fix round 1).
    /// </summary>
    [Fact]
    public async Task Approving_two_lines_on_the_same_contract_sums_the_allocated_volume()
    {
        var a = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseOnSameContractAsync(a, 20_000m, Today.AddDays(-30));

        var r = await _ctx.CreateApprovedAsync(39_000m, Today.AddDays(-2), (a, 600m), (b, 400m));

        Assert.Equal(WarehouseReconciliationStatus.Approved, (await _ctx.ReloadAsync(r.Key)).Status);
        var contract = await _ctx.Db.Context.PurchaseContracts.AsNoTracking()
            .SingleAsync(x => x.Key == a.PurchaseContractKey);
        Assert.Equal(1_000m, contract.AllocatedVolume);
    }

    [Fact]
    public async Task Approving_a_loss_on_a_sales_return_release_generates_only_the_loss()
    {
        await _ctx.SeedStockAsync(StorageTransactionType.SalesShipmentReturn, 29_400m, Today.AddDays(-30));
        var release = await _ctx.SeedReleaseAsync(29_400m, Today.AddDays(-30), origin: ReleaseOrigin.SalesReturn);

        var r = await _ctx.CreateApprovedAsync(29_000m, Today, (release, 400m));

        var line = await _ctx.Db.Context.WarehouseReconciliationReleases.AsNoTracking()
            .SingleAsync(x => x.WarehouseReconciliationKey == r.Key);
        Assert.Null(line.PurchaseStorageTransactionKey);
        Assert.NotNull(line.LossStorageTransactionKey);
        Assert.Equal(29_000m, (await _ctx.ReloadReleaseAsync(release.Key)).AvailableQuantity);
        Assert.False(await _ctx.Db.Context.PurchaseContractsAllocations.AnyAsync());
        Assert.Equal(29_000m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Approval_refuses_when_the_balance_changed_after_sending()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.DistributeAsync(r.Key, (release, 100m));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.PurchaseReturn, 40m, Today.AddDays(-15));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_MISMATCH", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
        Assert.False(await _ctx.Db.Context.StorageTransactions.AnyAsync(
            x => x.TransactionOrigin == TransactionCode.WarehouseReconciliation));
    }

    [Fact]
    public async Task Approval_refuses_when_the_release_was_shipped_meanwhile()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.DistributeAsync(r.Key, (release, 100m));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        // Embarque depois da data: não muda o saldo NA DATA, mas deixa só 50 hoje.
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 950m, Today.AddDays(-1));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASE_INSUFFICIENT_BALANCE", ex.Message);
    }

    /// <summary>
    /// GAC-1210 (30/10): a sobra vira uma liberação Sobra NOVA no contrato da liberação escolhida —
    /// é a porta de saída para a Expedição — e o romaneio Sobra(14) credita o armazém.
    /// </summary>
    [Fact]
    public async Task Approving_a_gain_creates_a_warehouse_gain_release_on_the_chosen_contract()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var referenceDate = Today.AddDays(-2);

        var r = await _ctx.CreateApprovedAsync(1_010m, referenceDate, (release, 10m));

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Approved, saved.Status);
        Assert.Null(saved.StorageTransactionKey);

        var line = await _ctx.Db.Context.WarehouseReconciliationReleases.AsNoTracking()
            .SingleAsync(x => x.WarehouseReconciliationKey == r.Key);
        Assert.Null(line.PurchaseStorageTransactionKey);
        var gain = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == line.LossStorageTransactionKey);
        Assert.Equal(StorageTransactionType.WarehouseGain, gain.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, gain.TransactionStatus);
        Assert.Equal(TransactionCode.WarehouseReconciliation, gain.TransactionOrigin);
        Assert.Equal(10m, gain.NetWeight);
        Assert.Equal(referenceDate, gain.TransactionDate);
        Assert.Null(gain.ShipmentReleaseKey);

        var created = await _ctx.Db.Context.ShipmentReleases.AsNoTracking()
            .SingleAsync(x => x.GeneratedByStorageTransactionKey == gain.Key);
        Assert.Equal(ReleaseOrigin.WarehouseGain, created.Origin);
        Assert.Equal(ReleaseStatus.Actived, created.Status);
        Assert.Equal(release.PurchaseContractKey, created.PurchaseContractKey);
        Assert.Equal(10m, created.ReleasedQuantity);
        Assert.Equal(0m, created.ShippedQuantity);
        Assert.Equal(WarehouseReconciliationsTestContext.ThirdPartyWarehouse, created.DeliveryLocationCode);
        Assert.Equal(referenceDate, created.ReleaseDate);
        Assert.Null(created.StorageAddressCode);

        // A liberação escolhida não muda e o contrato do produtor não recebe alocação.
        Assert.Equal(1_000m, (await _ctx.ReloadReleaseAsync(release.Key)).AvailableQuantity);
        Assert.False(await _ctx.Db.Context.PurchaseContractsAllocations.AnyAsync());

        // A próxima conferência já enxerga a sobra no saldo do sistema.
        var next = WarehouseReconciliationsTestContext.NewReconciliation(Guid.NewGuid(), 1_010m, Today);
        await _ctx.Guard().RefreshSnapshotAsync(next);
        Assert.Equal(1_010m, next.SystemBalance);
        Assert.Equal(0m, next.Difference);
    }

    [Fact]
    public void Gain_release_ships_without_purchase_leg_and_does_not_consume_the_contract()
    {
        Assert.True(ReleaseOriginRules.ShipsWithoutPurchaseLeg(ReleaseOrigin.WarehouseGain));
        Assert.False(ReleaseOriginRules.ConsumesPurchaseContract(ReleaseOrigin.WarehouseGain));
        Assert.False(ReleaseOriginRules.RequiresStorageAddress(ReleaseOrigin.WarehouseGain));
    }

    [Fact]
    public async Task Sending_a_gain_refuses_when_the_distribution_does_not_match_the_difference()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(1_010m);
        await _ctx.DistributeAsync(r.Key, (release, 4m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Gain_can_go_to_a_release_without_balance_today()
    {
        // Sobra costuma aparecer quando o armazém já embarcou tudo: a liberação está zerada hoje.
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 1_000m, Today.AddDays(-1));

        var r = await _ctx.CreateApprovedAsync(1_010m, Today.AddDays(-2), (release, 10m));

        Assert.Equal(WarehouseReconciliationStatus.Approved, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Approved_gain_can_be_cancelled_while_its_release_was_not_shipped()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(1_010m, Today.AddDays(-2), (release, 10m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "erro", "user");

        Assert.Equal(WarehouseReconciliationStatus.Cancelled, (await _ctx.ReloadAsync(r.Key)).Status);
        Assert.All(
            await _ctx.Db.Context.StorageTransactions.AsNoTracking()
                .Where(x => x.TransactionOrigin == TransactionCode.WarehouseReconciliation).ToListAsync(),
            x => Assert.Equal(StorageTransactionsStatus.Cancelled, x.TransactionStatus));
        var created = await _ctx.Db.Context.ShipmentReleases.AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.WarehouseGain);
        Assert.Equal(ReleaseStatus.Cancelled, created.Status);
    }

    [Fact]
    public async Task Approved_gain_cannot_be_cancelled_after_its_release_shipped()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(1_010m, Today.AddDays(-2), (release, 10m));
        var created = await _ctx.Db.Context.ShipmentReleases
            .SingleAsync(x => x.Origin == ReleaseOrigin.WarehouseGain);
        created.ShippedQuantity = 5m;
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Cancel().ExecuteAsync(r.Key, "erro", "user"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_GAIN_RELEASE_SHIPPED", ex.Message);
    }

    [Fact]
    public async Task Approval_refuses_non_in_approval()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL", ex.Message);
    }
}
