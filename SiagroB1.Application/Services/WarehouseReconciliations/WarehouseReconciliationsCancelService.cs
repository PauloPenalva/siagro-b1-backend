using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Rascunho cancela sem efeito. Aprovada cancela o romaneio de Perda/Sobra, e só se for a
/// ÚLTIMA aprovada do armazém+produto — desfazer uma do meio invalidaria o snapshot das seguintes.
/// </summary>
public class WarehouseReconciliationsCancelService(
    IUnitOfWork db,
    StorageTransactionsCancelService storageTransactionsCancelService,
    IStringLocalizer<Resource> resource,
    ILogger<WarehouseReconciliationsCancelService> logger)
{
    public async Task ExecuteAsync(Guid key, string? reason, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_CANCELLATION_REASON_REQUIRED"].Value);

        if (r.Status is not (WarehouseReconciliationStatus.Draft or WarehouseReconciliationStatus.Approved))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_CANNOT_CANCEL"].Value);

        if (r.Status == WarehouseReconciliationStatus.Approved)
            await EnsureApprovedCanBeUndoneAsync(r.Key, r.WarehouseCode, r.ItemCode, r.Difference);

        try
        {
            await db.BeginTransactionAsync();

            if (r.Status == WarehouseReconciliationStatus.Approved && r.StorageTransactionKey is Guid transactionKey)
            {
                await storageTransactionsCancelService.ExecuteAsync(
                    transactionKey, userName, TransactionCode.WarehouseReconciliation);
            }

            r.Status = WarehouseReconciliationStatus.Cancelled;
            r.CancellationReason = reason.Trim();
            r.CanceledAt = DateTime.Now;
            r.CanceledBy = userName;
            r.UpdatedAt = DateTime.Now;
            r.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao cancelar a conferência de saldo: {e.Message}");
        }
    }

    private async Task EnsureApprovedCanBeUndoneAsync(
        Guid key, string warehouseCode, string itemCode, decimal difference)
    {
        var latestKey = await db.Context.WarehouseReconciliations
            .AsNoTracking()
            .Where(x => x.WarehouseCode == warehouseCode &&
                        x.ItemCode == itemCode &&
                        x.Status == WarehouseReconciliationStatus.Approved)
            .OrderByDescending(x => x.ReferenceDate)
            .ThenByDescending(x => x.ApprovedAt)
            .ThenByDescending(x => x.RowId)
            .Select(x => x.Key)
            .FirstAsync();

        if (latestKey != key)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_LATEST"].Value);

        // Cancelar uma SOBRA tira grão do saldo: não pode deixar o armazém negativo hoje.
        if (difference > decimal.Zero)
        {
            var current = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                db.Context, warehouseCode, itemCode);

            if (current - difference < decimal.Zero)
                throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE"].Value);
        }
    }
}
