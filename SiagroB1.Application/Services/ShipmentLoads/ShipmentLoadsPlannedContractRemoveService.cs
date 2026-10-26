using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Remove um contrato planejado da carga (GAC-1185). <c>key</c> é a chave da LINHA planejada,
/// não a do contrato. Recusa carga cancelada, como a inclusão; os deletes de carga/contrato não passam por aqui.
/// </summary>
public class ShipmentLoadsPlannedContractRemoveService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsPlannedContractRemoveService> logger)
{
    public async Task ExecuteAsync(PlannedContractType type, Guid key, string user)
    {
        try
        {
            Guid loadKey;
            string? code;
            decimal? quantity;

            if (type == PlannedContractType.Purchase)
            {
                var row = await context.ShipmentLoadsPlannedPurchaseContracts
                              .Include(x => x.Contract)
                              .FirstOrDefaultAsync(x => x.Key == key)
                          ?? throw new NotFoundException("Contrato planejado não encontrado.");

                await EnsureNotCancelledAsync(row.ShipmentLoadKey);

                loadKey = row.ShipmentLoadKey;
                code = row.Contract?.Code;
                quantity = row.PlannedQuantity;
                context.ShipmentLoadsPlannedPurchaseContracts.Remove(row);
            }
            else
            {
                var row = await context.ShipmentLoadsPlannedSalesContracts
                              .Include(x => x.Contract)
                              .FirstOrDefaultAsync(x => x.Key == key)
                          ?? throw new NotFoundException("Contrato planejado não encontrado.");

                await EnsureNotCancelledAsync(row.ShipmentLoadKey);

                loadKey = row.ShipmentLoadKey;
                code = row.Contract?.Code;
                quantity = row.PlannedQuantity;
                context.ShipmentLoadsPlannedSalesContracts.Remove(row);
            }

            changeLog.Register(
                loadKey,
                type == PlannedContractType.Purchase
                    ? ShipmentLoadChangeLogFields.PlannedPurchaseContract
                    : ShipmentLoadChangeLogFields.PlannedSalesContract,
                ShipmentLoadChangeLogFields.DescribePlannedContract(code, quantity),
                null,
                user);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    private async Task EnsureNotCancelledAsync(Guid loadKey)
    {
        var load = await context.ShipmentLoads
            .Where(x => x.Key == loadKey)
            .Select(x => new { x.Code, x.Status })
            .FirstOrDefaultAsync();

        if (load?.Status == ShipmentLoadStatus.Cancelled)
            throw new ApplicationException($"A carga {load.Code} está cancelada.");
    }
}
