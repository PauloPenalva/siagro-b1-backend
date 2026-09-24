using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Marca a carga como descarregada no destino (GAC-1171, melhorias).
/// </summary>
/// <remarks>
/// Grava SÓ a marca <see cref="ShipmentLoad.IsDischarged"/>. O status continua saindo de
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/>, que é o escritor único. Por isso o
/// resultado pode ser Concluída em vez de Descarregada, quando a Conferência de Entregas já
/// estiver toda encerrada. O log registra o status RESULTANTE, não o pedido.
/// <para>
/// Não exige ticket de descarga: decisão do usuário. O ticket pode nunca chegar, e quem sabe que a
/// mercadoria foi entregue é a operação. O par de desfazer é <see cref="ShipmentLoadsUndoDischargedService"/>,
/// pelo mesmo motivo do Concluir/Reabrir da Remoção: um encerramento manual precisa de um desfazer manual.
/// </para>
/// </remarks>
public class ShipmentLoadsMarkDischargedService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.LoadType == ShipmentLoadType.Removal)
            throw new ApplicationException(
                $"A carga {load.Code} é do tipo Remoção e não passa por descarga.");

        if (load.Status != ShipmentLoadStatus.Invoiced)
        {
            var reason = load.Status switch
            {
                ShipmentLoadStatus.Discharged => "já está marcada como descarregada",
                ShipmentLoadStatus.Completed => "já foi concluída",
                ShipmentLoadStatus.PartiallyInvoiced => "ainda tem saldo a faturar",
                ShipmentLoadStatus.Cancelled => "está cancelada",
                ShipmentLoadStatus.Returned => "foi devolvida ao armazém",
                ShipmentLoadStatus.InTransshipment => "está em transbordo",
                _ => "ainda não foi faturada",
            };

            throw new ApplicationException($"A carga {load.Code} {reason}.");
        }

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            load.IsDischarged = true;

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            // A trava de cima lê o status PERSISTIDO, que pode estar defasado. Quem decide é o
            // recalculado: fora de Descarregada e Concluída, a marca não pegou (o recálculo a
            // limpa quando a carga sai de Faturada), e gravar log e movimento de uma descarga
            // que não aconteceu seria mentir. Lança dentro do try para desfazer a transação.
            if (load.Status is not (ShipmentLoadStatus.Discharged or ShipmentLoadStatus.Completed))
                throw new ApplicationException(
                    $"A carga {load.Code} não está faturada: recalcule o saldo da carga.");

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
                ShipmentLoadMovementType.Discharged,
                decimal.Zero,
                load.AvailableQuantity,
                "Carga marcada como descarregada no destino.",
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
