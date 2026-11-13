using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Reabre a transferência entre armazéns de terceiros (GAC-1176): desfaz a conclusão e devolve a
/// carga a "em transbordo" — de onde ela pode ser estornada ou seguir viagem.
/// </summary>
/// <remarks>
/// Par de <see cref="ShipmentLoadsTransshipmentCompleteService"/>: um encerramento manual precisa
/// de um desfazer manual, e aqui ele é o ÚNICO caminho de volta — cancelar e excluir a carga com
/// transbordo são recusados, e o estorno do transbordo exige reabrir antes.
/// <para>
/// ⚠️ <b>Recusado quando a liberação gerada pela entrada já foi embarcada</b>: a mercadoria saiu
/// do armazém de destino noutra carga. Reabrir não corromperia saldo, mas deixaria esta carga "em
/// transbordo" sem estoque e com o estorno bloqueado pela mesma razão — um beco sem saída.
/// </para>
/// <para>
/// O <see cref="TransshipmentType.Transfer"/> NÃO volta a Transbordo: a parada continua sendo uma
/// transferência, só não está mais concluída.
/// </para>
/// </remarks>
public class ShipmentLoadsTransshipmentReopenService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid transshipmentKey, string userName)
    {
        var transshipment = await db.Context.ShipmentLoadsTransshipments
                                 .FirstOrDefaultAsync(x => x.Key == transshipmentKey) ??
                             throw new NotFoundException(
                                 $"Shipment load transshipment not found key {transshipmentKey}");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == transshipment.ShipmentLoadKey) ??
                   throw new NotFoundException(
                       $"Shipment load not found key {transshipment.ShipmentLoadKey}");

        // TODA a validação antes de qualquer escrita.
        ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);

        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new ApplicationException($"A carga {load.Code} está cancelada.");

        if (transshipment.FinalDestinationAt == null)
            throw new ApplicationException(
                $"A transferência {transshipment.Sequence} da carga {load.Code} não foi concluída.");

        await ShipmentLoadTransshipmentRules.EnsureIsTheLastOneAsync(db.Context, transshipment, "reabrir");

        var releaseShipped = transshipment.EntryStorageTransactionKey is { } entryKey &&
                             await db.Context.ShipmentReleases.AnyAsync(x =>
                                 x.GeneratedByStorageTransactionKey == entryKey &&
                                 x.ShippedQuantity > ShipmentLoadTransshipmentRules.Tolerance);

        if (releaseShipped)
            throw new ApplicationException(
                $"A liberação da transferência {transshipment.Sequence} já foi embarcada no armazém " +
                "de destino. Estorne a Expedição de saída antes de reabrir a transferência.");

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            transshipment.FinalDestinationAt = null;
            transshipment.FinalDestinationBy = null;
            transshipment.UpdatedAt = DateTime.Now;
            transshipment.UpdatedBy = userName;

            // Recalcular só DEPOIS do SaveChanges que apagou o fato — o recálculo consulta o banco
            // e não enxerga o change tracker.
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedBy = userName;

            if (load.Status != previousStatus)
            {
                changeLog.Register(
                    load.Key,
                    ShipmentLoadChangeLogFields.Status,
                    ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                    ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
                    userName);
            }

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransferReopened,
                decimal.Zero,
                load.AvailableQuantity,
                $"Transferência {transshipment.Sequence} reaberta no armazém " +
                $"({transshipment.WarehouseCode}) {transshipment.WarehouseName}.",
                userName,
                movementContext: new ShipmentLoadMovementContext(
                    WarehouseCode: transshipment.WarehouseCode,
                    WarehouseName: transshipment.WarehouseName,
                    StorageTransactionKey: transshipment.EntryStorageTransactionKey));

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
