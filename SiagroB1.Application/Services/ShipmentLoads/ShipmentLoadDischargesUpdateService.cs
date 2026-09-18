using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Altera um ticket de descarga já registrado (GAC-1171). Nota e item não mudam: apontar o ticket
/// para outra linha é excluir e registrar de novo, senão a soma da linha antiga fica órfã.
/// </summary>
public class ShipmentLoadDischargesUpdateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesUpdateService> logger)
{
    public async Task ExecuteAsync(
        Guid dischargeKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        string? comments,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(quantity);

            var discharge = await context.ShipmentLoadsDischarges
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var before = ShipmentLoadChangeLogFields.DescribeDischarge(
                discharge.TicketNumber, discharge.DischargedQuantity);

            discharge.TicketNumber = ticket;
            discharge.DischargeDate = dischargeDate.Date;
            discharge.DischargedQuantity = quantity;
            discharge.Comments = comments;
            discharge.UpdatedAt = DateTime.Now;
            discharge.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Discharge,
                before,
                ShipmentLoadChangeLogFields.DescribeDischarge(ticket, quantity),
                userName);

            if (discharge.SalesInvoiceItemKey.HasValue)
                await recalculate.RecalculateAsync(
                    load.Key, [discharge.SalesInvoiceItemKey.Value]);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
