using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Cancela a carga e devolve os romaneios para a Montagem.
/// </summary>
/// <remarks>
/// A trava é pelas NOTAS ligadas, nunca pelo status da carga: durante o faturamento parcial a
/// carga continua <c>PartiallyInvoiced</c> e o status sozinho não protegeria nada. Só cancela
/// se todo documento de saída da carga estiver <c>Cancelled</c> — ou se não houver nenhum.
/// <para>
/// Devolver o romaneio para a Montagem é exatamente zerar <c>ShipmentLoadKey</c> e voltar o
/// <c>TransactionStatus</c> para <c>Confirmed</c>, que é o filtro daquela tela. Romaneio
/// <c>Cancelled</c> ou <c>Returned</c> nunca é reescrito: esses estados são dele, não projeção
/// da carga.
/// </para>
/// </remarks>
public class ShipmentLoadsCancelService(
    IUnitOfWork db,
    ShipmentLoadsCompositionGuardService compositionGuard,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task ExecuteAsync(Guid key, string cancellationReason, string userName)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ApplicationException("Informe o motivo do cancelamento.");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new ApplicationException("Carga já cancelada.");

        await compositionGuard.EnsureCanChangeCompositionAsync(load);

        var shipments = await db.Context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        try
        {
            await db.BeginTransactionAsync();

            load.Status = ShipmentLoadStatus.Cancelled;
            load.InvoicedQuantity = decimal.Zero;
            load.CancellationReason = cancellationReason.Trim();
            load.CanceledAt = DateTime.Now;
            load.CanceledBy = userName;
            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            foreach (var shipment in shipments)
            {
                shipment.ShipmentLoadKey = null;

                if (shipment.TransactionStatus is not (StorageTransactionsStatus.Cancelled
                    or StorageTransactionsStatus.Returned))
                {
                    shipment.TransactionStatus = StorageTransactionsStatus.Confirmed;
                }

                shipment.UpdatedAt = DateTime.Now;
                shipment.UpdatedBy = userName;
            }

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Cancelled,
                decimal.Zero,
                decimal.Zero,
                $"Carga cancelada. Motivo: {load.CancellationReason}. " +
                $"{shipments.Count} romaneio(s) devolvido(s) à Montagem de Carga.",
                userName);

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
