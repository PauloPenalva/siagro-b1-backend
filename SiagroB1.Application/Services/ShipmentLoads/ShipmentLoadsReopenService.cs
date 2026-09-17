using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Desfaz a conclusão de uma carga de remoção, devolvendo-a à operação.
/// </summary>
/// <remarks>
/// O status de volta NÃO é fixo em <c>Open</c>: vem do recálculo, que devolve <c>Planned</c> se
/// a carga estiver sem entradas. Fixar <c>Open</c> deixaria uma carga vazia fora do
/// planejamento — o mesmo erro que o primeiro ramo de <c>ResolveStatus</c> existe para evitar.
/// </remarks>
public class ShipmentLoadsReopenService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.Status != ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                $"A carga {load.Code} não está concluída.");

        try
        {
            await db.BeginTransactionAsync();

            // Sai de Completed ANTES do recálculo: ResolveRemoval não reescreve o status
            // terminal, então reabrir com ele no lugar não mudaria nada.
            load.Status = ShipmentLoadStatus.Open;

            await ShipmentLoadsRecalculateTotalService.RecalculateAsync(db.Context, load.Key);
            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Status,
                ShipmentLoadChangeLogFields.DescribeStatus(ShipmentLoadStatus.Completed),
                ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
                userName);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Reopened,
                decimal.Zero,
                load.TotalQuantity,
                "Conclusão da carga de remoção desfeita.",
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
