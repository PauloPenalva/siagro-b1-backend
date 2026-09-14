using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Aprova a conferência e gera o romaneio de Perda(13)/Sobra(14) — já Confirmado, sem contrato,
/// sem lote, com a data de referência. É o ÚNICO caminho que cria esses tipos.
/// </summary>
public class WarehouseReconciliationsApprovalService(
    IUnitOfWork db,
    WarehouseReconciliationsGuardService guard,
    StorageTransactionsCreateService storageTransactionsCreateService,
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

        var quantity = Math.Abs(r.Difference);

        // Mesma regra da confirmação de embarque: o armazém nunca fica negativo HOJE, ainda que a
        // perda tenha sido apurada numa data passada.
        if (r.Difference < decimal.Zero)
        {
            var current = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                db.Context, r.WarehouseCode, r.ItemCode);

            if (quantity > current)
                throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_LOSS_EXCEEDS_BALANCE"].Value);
        }

        try
        {
            await db.BeginTransactionAsync();

            var transaction = new StorageTransaction
            {
                TransactionDate = r.ReferenceDate,
                TransactionStatus = StorageTransactionsStatus.Confirmed,
                TransactionType = r.Difference < decimal.Zero
                    ? StorageTransactionType.WarehouseLoss
                    : StorageTransactionType.WarehouseGain,
                GrossWeight = quantity,
                NetWeight = quantity,
                AvaiableVolumeToAllocate = decimal.Zero,
                BranchCode = r.BranchCode,
                CardCode = r.CardCode ?? r.WarehouseCode,
                ItemCode = r.ItemCode,
                UnitOfMeasureCode = r.UnitOfMeasureCode,
                WarehouseCode = r.WarehouseCode,
                StorageAddressCode = null,
                Comments = $"Conferência de saldo de armazém {r.Code}",
            };

            await storageTransactionsCreateService.ExecuteAsync(
                transaction, userName, TransactionCode.WarehouseReconciliation, CommitMode.Deferred);

            r.StorageTransactionKey = transaction.Key;
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
    }
}
