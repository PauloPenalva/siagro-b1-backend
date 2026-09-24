using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Desfaz a marca de descarregada (GAC-1171, melhorias): o "voltar ao status anterior".
/// </summary>
/// <remarks>
/// O status anterior NÃO é guardado: ele sai do recálculo, como no <c>ShipmentLoadsReopenService</c>.
/// Guardar um "status anterior" criaria uma segunda fonte de verdade, e ela mentiria assim que
/// uma nota fosse cancelada no meio do caminho.
/// <para>
/// Recusado com a carga Concluída: ali a marca não decide nada (a Conferência vence), e
/// desfazê-la não mudaria o status. O caminho é estornar a conferência de entrega.
/// </para>
/// </remarks>
public class ShipmentLoadsUndoDischargedService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.Status == ShipmentLoadStatus.Completed && load.LoadType == ShipmentLoadType.Normal)
            throw new ApplicationException(
                $"A carga {load.Code} está concluída. " +
                "Estorne a conferência de entrega antes de desfazer a descarga.");

        if (load.Status != ShipmentLoadStatus.Discharged)
            throw new ApplicationException(
                $"A carga {load.Code} não está marcada como descarregada.");

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            load.IsDischarged = false;

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Status,
                ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
                userName);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.DischargeUndone,
                decimal.Zero,
                load.AvailableQuantity,
                "Marcação de descarregada desfeita.",
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
