using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Conclui uma carga de REMOÇÃO (GAC-1175): a remoção terminou, a carga sai da operação.
/// </summary>
/// <remarks>
/// É a terceira exceção ao "escritor único" do status, ao lado de <c>Cancelled</c> e do
/// <c>Planned</c> da criação — e existe porque "a remoção acabou" não é derivável de nenhum
/// número: a carga de remoção não tem faturamento que a encerre, e uma mesma carga pode receber
/// entradas ao longo do dia antes de fechar. Só o usuário sabe quando é o fim.
/// <para>
/// Por isso o par com <see cref="ShipmentLoadsReopenService"/>: um encerramento manual precisa
/// de um desfazer manual, senão o engano de um clique exige cancelar a carga inteira.
/// </para>
/// </remarks>
public class ShipmentLoadsCompleteService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.LoadType != ShipmentLoadType.Removal)
            throw new ApplicationException(
                $"A carga {load.Code} não é do tipo Remoção: ela é encerrada pelo faturamento.");

        if (load.Status != ShipmentLoadStatus.Open)
        {
            var reason = load.Status switch
            {
                ShipmentLoadStatus.Planned =>
                    "não possui romaneios de recebimento vinculados",
                ShipmentLoadStatus.Completed => "já foi concluída",
                ShipmentLoadStatus.Cancelled => "está cancelada",
                _ => "não pode ser concluída",
            };

            throw new ApplicationException($"A carga {load.Code} {reason}.");
        }

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            load.Status = ShipmentLoadStatus.Completed;
            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Status,
                ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                ShipmentLoadChangeLogFields.DescribeStatus(ShipmentLoadStatus.Completed),
                userName);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Completed,
                decimal.Zero,
                load.TotalQuantity,
                "Carga de remoção concluída.",
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
