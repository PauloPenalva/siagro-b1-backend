using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Registra um ticket de descarga na carga (GAC-1171), contra uma linha de documento de saída.
/// </summary>
public class ShipmentLoadDischargesCreateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesCreateService> logger)
{
    public async Task<ShipmentLoadDischarge> ExecuteAsync(
        Guid loadKey,
        Guid salesInvoiceKey,
        Guid salesInvoiceItemKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        string? comments,
        Guid? attachmentKey,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(quantity);

            var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == loadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var invoice = await context.SalesInvoices
                .FirstOrDefaultAsync(x => x.Key == salesInvoiceKey)
                ?? throw new NotFoundException("Documento de saída não encontrado.");

            if (invoice.ShipmentLoadKey != loadKey)
                throw new DefaultException(
                    "O documento de saída informado não pertence a esta carga.");

            if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
                throw new DefaultException(
                    "Não é possível registrar descarga em documento de saída cancelado.");

            var item = await context.SalesInvoicesItems
                .FirstOrDefaultAsync(x => x.Key == salesInvoiceItemKey)
                ?? throw new NotFoundException("Item do documento de saída não encontrado.");

            if (item.SalesInvoiceKey != salesInvoiceKey)
                throw new DefaultException(
                    "O item informado não pertence ao documento de saída informado.");

            var discharge = new ShipmentLoadDischarge
            {
                ShipmentLoadKey = loadKey,
                SalesInvoiceKey = salesInvoiceKey,
                SalesInvoiceItemKey = salesInvoiceItemKey,
                TicketNumber = ticket,
                DischargeDate = dischargeDate.Date,
                DischargedQuantity = quantity,
                Comments = comments,
                AttachmentKey = attachmentKey,
                CreatedAt = DateTime.Now,
                CreatedBy = userName,
            };

            await context.AddAsync(discharge);

            changeLog.Register(
                loadKey,
                ShipmentLoadChangeLogFields.Discharge,
                null,
                ShipmentLoadChangeLogFields.DescribeDischarge(ticket, quantity),
                userName);

            await recalculate.RecalculateAsync(loadKey, [salesInvoiceItemKey]);

            await context.SaveChangesAsync();

            return discharge;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
