using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Exclui um ticket de descarga (GAC-1171). O ticket excluído fica no log — é o que permite
/// reconstituir o peso que já foi lançado e depois retirado.
/// </summary>
public class ShipmentLoadDischargesDeleteService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesDeleteService> logger)
{
    public async Task ExecuteAsync(Guid dischargeKey, string userName)
    {
        try
        {
            var discharge = await context.ShipmentLoadsDischarges
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var itemKey = discharge.SalesInvoiceItemKey;

            context.ShipmentLoadsDischarges.Remove(discharge);

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Discharge,
                ShipmentLoadChangeLogFields.DescribeDischarge(
                    discharge.TicketNumber, discharge.DischargedQuantity),
                null,
                userName);

            if (itemKey.HasValue)
                await recalculate.RecalculateAsync(load.Key, [itemKey.Value]);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
