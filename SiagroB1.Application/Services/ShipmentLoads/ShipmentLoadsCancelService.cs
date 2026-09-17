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
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
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

        // GAC-1175: a conclusão da carga de remoção é a afirmação de que a remoção terminou.
        // Cancelar por cima dela apagaria esse fecho sem que o usuário o desfizesse.
        if (load.Status == ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                $"A carga {load.Code} já foi concluída. Reabra-a antes de cancelá-la.");

        await compositionGuard.EnsureCanChangeCompositionAsync(load);

        var shipments = await db.Context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            load.Status = ShipmentLoadStatus.Cancelled;
            load.InvoicedQuantity = decimal.Zero;
            load.CancellationReason = cancellationReason.Trim();
            load.CanceledAt = DateTime.Now;
            load.CanceledBy = userName;
            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Status,
                ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                ShipmentLoadChangeLogFields.DescribeStatus(ShipmentLoadStatus.Cancelled),
                userName);

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.CancellationReason,
                null,
                load.CancellationReason,
                userName);

            foreach (var shipment in shipments)
            {
                shipment.ShipmentLoadKey = null;

                // Só a carga de expedição projetou status no romaneio (GAC-1175).
                if (load.LoadType != ShipmentLoadType.Removal &&
                    shipment.TransactionStatus is not (StorageTransactionsStatus.Cancelled
                    or StorageTransactionsStatus.Returned))
                {
                    shipment.TransactionStatus = StorageTransactionsStatus.Confirmed;
                }

                shipment.UpdatedAt = DateTime.Now;
                shipment.UpdatedBy = userName;
            }

            var released = load.LoadType == ShipmentLoadType.Removal
                ? $"{shipments.Count} romaneio(s) de recebimento liberado(s)."
                : $"{shipments.Count} romaneio(s) devolvido(s) à Montagem de Carga.";

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Cancelled,
                decimal.Zero,
                decimal.Zero,
                $"Carga cancelada. Motivo: {load.CancellationReason}. " + released,
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
