using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Registra um ticket de descarga na carga (GAC-1171), RATEADO entre linhas de documentos de saída.
/// </summary>
public class ShipmentLoadDischargesCreateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsClosureHookService closureHook,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesCreateService> logger)
{
    public async Task<ShipmentLoadDischarge> ExecuteAsync(
        Guid loadKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        IReadOnlyList<ShipmentLoadDischargeLine> lines,
        string? comments,
        Guid? attachmentKey,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            var total = ShipmentLoadDischargeRules.RoundQuantity(quantity);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(total);
            var distribution = ShipmentLoadDischargeRules.NormalizeDistribution(total, lines);

            var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == loadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var shares = await ShipmentLoadDischargeRules.ResolveLinesAsync(context, loadKey, distribution);

            var discharge = new ShipmentLoadDischarge
            {
                ShipmentLoadKey = loadKey,
                TicketNumber = ticket,
                DischargeDate = dischargeDate.Date,
                DischargedQuantity = total,
                Comments = comments,
                AttachmentKey = attachmentKey,
                CreatedAt = DateTime.Now,
                CreatedBy = userName,
            };

            foreach (var share in shares)
                discharge.Items.Add(share);

            await context.AddAsync(discharge);

            changeLog.Register(
                loadKey,
                ShipmentLoadChangeLogFields.Discharge,
                null,
                ShipmentLoadChangeLogFields.DescribeDischarge(ticket, total, Describe(discharge.Items)),
                userName);

            await recalculate.RecalculateAsync(loadKey, shares.Select(x => x.SalesInvoiceItemKey));

            // Depois das somas: a Descarregada lê o TicketDeliveredQuantity que acabou de mudar.
            await closureHook.ApplyAsync(loadKey, userName);

            await context.SaveChangesAsync();

            return discharge;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    internal static IEnumerable<(string? InvoiceNumber, decimal Quantity)> Describe(
        IEnumerable<ShipmentLoadDischargeItem> items) =>
        items.Select(x => (x.SalesInvoice?.InvoiceNumber, x.Quantity));
}
