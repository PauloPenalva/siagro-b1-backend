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
/// Rascunho cancela sem efeito. Aprovada desfaz o consumo das liberações (GAC-1164 §9.7) — ou
/// cancela o romaneio único da conferência legada — e só se for a ÚLTIMA aprovada do
/// armazém+produto.
/// </summary>
public class WarehouseReconciliationsCancelService(
    IUnitOfWork db,
    StorageTransactionsCancelService storageTransactionsCancelService,
    PurchaseContractsAllocationDeleteService allocationDelete,
    ShipmentReleasesRecalculateShippedService recalcShipped,
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

        var wasApproved = r.Status == WarehouseReconciliationStatus.Approved;

        var lines = await db.Context.WarehouseReconciliationReleases
            .Where(x => x.WarehouseReconciliationKey == r.Key)
            .ToListAsync();

        if (wasApproved)
            await EnsureApprovedCanBeUndoneAsync(r, lines);

        try
        {
            await db.BeginTransactionAsync();

            if (wasApproved)
            {
                if (lines.Count == 0 && r.StorageTransactionKey is Guid legacyKey)
                {
                    // Aprovada antes da revisão de 17/09/2026: um romaneio só, sem liberação.
                    await storageTransactionsCancelService.ExecuteAsync(
                        legacyKey, userName, TransactionCode.WarehouseReconciliation);
                }
                else
                {
                    await UndoLinesAsync(lines, userName);
                }
            }

            r.Status = WarehouseReconciliationStatus.Cancelled;
            r.CancellationReason = reason.Trim();
            r.CanceledAt = DateTime.Now;
            r.CanceledBy = userName;
            r.UpdatedAt = DateTime.Now;
            r.UpdatedBy = userName;

            await db.SaveChangesAsync();

            // Depois do SaveChanges (a consulta precisa enxergar o Cancelled) e dentro da transação,
            // como em ShippingTransactionsReverseService: falha aqui reverte o cancelamento inteiro.
            // Só quando havia efeito a desfazer (Aprovada) — um Rascunho nunca moveu o embarcado.
            if (wasApproved)
                foreach (var releaseKey in lines.Select(x => x.ShipmentReleaseKey).Distinct())
                    await recalcShipped.RecalculateAsync(releaseKey);

            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao cancelar a conferência de saldo: {e.Message}");
        }
    }

    /// <summary>
    /// Desfaz cada linha como o estorno da Expedição: remove a alocação da Compra, cancela a Compra e
    /// a Perda escrevendo o status direto (StorageTransactionsCancelService recusaria a Compra por
    /// ter alocação, e é por isso que o recálculo da liberação é explícito no chamador).
    /// </summary>
    private async Task UndoLinesAsync(List<WarehouseReconciliationRelease> lines, string userName)
    {
        foreach (var line in lines)
        {
            if (line.PurchaseStorageTransactionKey is Guid purchaseKey)
            {
                var allocationKey = await db.Context.PurchaseContractsAllocations
                    .Where(x => x.StorageTransactionKey == purchaseKey)
                    .Select(x => x.Key)
                    .FirstOrDefaultAsync();

                if (allocationKey != Guid.Empty)
                {
                    await allocationDelete.ExecuteAsync(allocationKey, userName, CommitMode.Deferred);

                    // Flush por linha (fix round 1, GAC-1164): quando duas linhas caem no MESMO
                    // contrato, PurchaseContractsAllocationDeleteService deriva o AllocatedVolume
                    // somando o que resta PERSISTIDO (x.Key != alloc.Key) — sem salvar aqui a remoção
                    // desta linha fica só rastreada, e a soma da próxima linha ainda a conta (sobra
                    // volume fantasma).
                    await db.SaveChangesAsync();
                }

                await MarkCancelledAsync(purchaseKey, userName);
            }

            if (line.LossStorageTransactionKey is Guid lossKey)
                await MarkCancelledAsync(lossKey, userName);
        }
    }

    private async Task MarkCancelledAsync(Guid key, string userName)
    {
        var transaction = await db.Context.StorageTransactions.FirstAsync(x => x.Key == key);
        transaction.TransactionStatus = StorageTransactionsStatus.Cancelled;
        transaction.CanceledAt = DateTime.Now;
        transaction.CanceledBy = userName;
    }

    private async Task EnsureApprovedCanBeUndoneAsync(
        WarehouseReconciliation r, List<WarehouseReconciliationRelease> lines)
    {
        var latestKey = await db.Context.WarehouseReconciliations
            .AsNoTracking()
            .Where(x => x.WarehouseCode == r.WarehouseCode &&
                        x.ItemCode == r.ItemCode &&
                        x.Status == WarehouseReconciliationStatus.Approved)
            .OrderByDescending(x => x.ReferenceDate)
            .ThenByDescending(x => x.ApprovedAt)
            .ThenByDescending(x => x.RowId)
            .Select(x => x.Key)
            .FirstAsync();

        if (latestKey != r.Key)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_LATEST"].Value);

        // Legado (antes da revisão de 17/09/2026): sem linha de distribuição, "Difference > 0" é
        // uma SOBRA aprovada pela regra pré-revisão. Cancelá-la tira grão do saldo — não pode
        // deixar o armazém negativo hoje. Com linhas, quem decide é UndoLinesAsync (só perda).
        if (lines.Count == 0 && r.Difference > decimal.Zero)
        {
            var current = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                db.Context, r.WarehouseCode, r.ItemCode);

            if (current - r.Difference < decimal.Zero)
                throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE"].Value);
        }
    }
}
