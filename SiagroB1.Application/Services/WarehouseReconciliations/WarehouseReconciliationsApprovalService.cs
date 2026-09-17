using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Aprova a conferência e CONSOME as liberações da distribuição (GAC-1164 §9.5). A quebra é custo da
/// empresa, e o grão perdido não pode continuar a embarcar.
/// </summary>
/// <remarks>
/// Por linha, espelhando <c>ShippingTransactionsCreateService</c> com a Perda no lugar da Saída:
/// <list type="bullet">
/// <item><b>Standard</b> — Compra(8) confirmada e alocada no contrato (o produtor entregou; a
/// liberação é consumida) + Perda(13) copiada dela, que dá a saída no armazém. No saldo por romaneios
/// as duas se anulam, como o par da Expedição.</item>
/// <item><b>Sem perna de compra</b> (transferência / devolução de venda) — o grão já entrou antes; só
/// a Perda(13), que consome a liberação por
/// <see cref="ShipmentReleasesRecalculateShippedService.ShippedSign"/>.</item>
/// </list>
/// Tudo em <c>CommitMode.Deferred</c> numa transação; o recálculo das liberações roda DEPOIS do
/// commit, porque a Perda nasce como cópia tipada de Compra e só é retipada em memória — recalcular
/// antes contaria a Compra duas vezes (mesmo motivo documentado na Expedição).
/// </remarks>
public class WarehouseReconciliationsApprovalService(
    IUnitOfWork db,
    WarehouseReconciliationsGuardService guard,
    StorageTransactionsCreateService storageCreate,
    StorageTransactionsConfirmedService storageConfirmed,
    StorageTransactionsCopyService storageCopy,
    PurchaseContractsAllocationCreateService allocationCreate,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    IStringLocalizer<Resource> resource,
    ILogger<WarehouseReconciliationsApprovalService> logger)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        // Tudo abaixo roda antes da transação: o catch embrulharia a mensagem de negócio.
        if (r.Status != WarehouseReconciliationStatus.InApproval)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL"].Value);

        await guard.EnsureCanPersistAsync(r);
        await guard.RefreshSnapshotAsync(r);

        if (r.Difference == decimal.Zero)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE"].Value);

        var lines = await guard.EnsureLossDistributionAsync(r);

        var releaseKeys = lines.Select(l => l.ShipmentReleaseKey).ToList();
        var releases = await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => releaseKeys.Contains(x.Key))
            .Select(x => new { x.Key, x.Origin, x.PurchaseContractKey, x.PurchaseContract!.CardCode })
            .ToDictionaryAsync(x => x.Key);

        try
        {
            await db.BeginTransactionAsync();

            foreach (var line in lines)
            {
                var release = releases[line.ShipmentReleaseKey];

                if (ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin))
                {
                    var loss = NewTransaction(r, line, StorageTransactionType.WarehouseLoss, r.CardCode ?? r.WarehouseCode);
                    loss.TransactionStatus = StorageTransactionsStatus.Confirmed;
                    await storageCreate.ExecuteAsync(loss, userName, TransactionCode.WarehouseReconciliation, CommitMode.Deferred);
                    line.LossStorageTransactionKey = loss.Key;
                }
                else
                {
                    // Perna comercial: o produtor da liberação, como na Expedição.
                    var purchase = NewTransaction(r, line, StorageTransactionType.Purchase, release.CardCode);
                    await storageCreate.ExecuteAsync(purchase, userName, TransactionCode.WarehouseReconciliation, CommitMode.Deferred);
                    await storageConfirmed.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);
                    await allocationCreate.ExecuteAsync(
                        release.PurchaseContractKey, purchase, purchase.NetWeight, userName, CommitMode.Deferred);

                    // Flush por linha (fix round 1, GAC-1164): a próxima linha pode cair no MESMO
                    // contrato, e ApplyAllocationAsync deriva o AllocatedVolume somando as alocações
                    // já PERSISTIDAS + a desta chamada — sem salvar aqui a alocação desta linha fica
                    // só rastreada, e a soma da próxima linha não a enxerga (perde volume em silêncio).
                    await db.SaveChangesAsync();

                    var loss = await storageCopy.ExecuteAsync(purchase, userName, CommitMode.Deferred);
                    loss.TransactionType = StorageTransactionType.WarehouseLoss;
                    loss.TransactionStatus = StorageTransactionsStatus.Confirmed;
                    // A cópia nasce com a origem de romaneio avulso; a Conferência é a dona, e é isso
                    // que faz Cancelar/Estornar da tela de Romaneios recusarem mexer nela.
                    loss.TransactionOrigin = TransactionCode.WarehouseReconciliation;
                    // A cópia nasce com a data de HOJE; toda a conferência é datada na referência.
                    loss.TransactionDate = r.ReferenceDate;
                    loss.TransactionTime = purchase.TransactionTime;
                    loss.NetWeight = purchase.NetWeight;
                    loss.GrossWeight = purchase.NetWeight;
                    loss.AvaiableVolumeToAllocate = decimal.Zero;
                    loss.StorageAddressCode = null;

                    line.PurchaseStorageTransactionKey = purchase.Key;
                    line.LossStorageTransactionKey = loss.Key;
                }
            }

            r.Status = WarehouseReconciliationStatus.Approved;
            r.ApprovalComments = comments;
            r.ApprovedAt = DateTime.Now;
            r.ApprovedBy = userName;
            r.UpdatedAt = DateTime.Now;
            r.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao aprovar a conferência de saldo: {e.Message}");
        }

        foreach (var releaseKey in releases.Keys)
            await recalcShipped.RecalculateAsync(releaseKey);
    }

    private static StorageTransaction NewTransaction(
        WarehouseReconciliation r, WarehouseReconciliationRelease line, StorageTransactionType type, string cardCode) => new()
        {
            TransactionDate = r.ReferenceDate,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionType = type,
            GrossWeight = line.Quantity,
            NetWeight = line.Quantity,
            AvaiableVolumeToAllocate = decimal.Zero,
            BranchCode = r.BranchCode,
            CardCode = cardCode,
            ItemCode = r.ItemCode,
            UnitOfMeasureCode = r.UnitOfMeasureCode,
            WarehouseCode = r.WarehouseCode,
            StorageAddressCode = null,
            ShipmentReleaseKey = line.ShipmentReleaseKey,
            Comments = $"Conferência de saldo de armazém {r.Code}",
        };
}
