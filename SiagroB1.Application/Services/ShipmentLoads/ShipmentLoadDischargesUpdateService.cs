using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Altera um ticket de descarga já registrado (GAC-1171), inclusive o RATEIO: as parcelas enviadas
/// substituem as gravadas.
/// </summary>
public class ShipmentLoadDischargesUpdateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsClosureHookService closureHook,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesUpdateService> logger)
{
    public async Task ExecuteAsync(
        Guid dischargeKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        IReadOnlyList<ShipmentLoadDischargeLine> lines,
        string? comments,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            var total = ShipmentLoadDischargeRules.RoundQuantity(quantity);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(total);
            var distribution = ShipmentLoadDischargeRules.NormalizeDistribution(total, lines);

            var discharge = await context.ShipmentLoadsDischarges
                .Include(x => x.Items)
                .ThenInclude(x => x.SalesInvoice)
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var shares = await ShipmentLoadDischargeRules.ResolveLinesAsync(
                context,
                load.Key,
                distribution,
                discharge.Items.ToDictionary(x => x.SalesInvoiceItemKey, x => x.Quantity));

            var before = ShipmentLoadChangeLogFields.DescribeDischarge(
                discharge.TicketNumber,
                discharge.DischargedQuantity,
                ShipmentLoadDischargesCreateService.Describe(discharge.Items).ToList());

            // Linhas que saem também precisam ser recalculadas: senão o peso fica pendurado nelas.
            var touchedItemKeys = discharge.Items.Select(x => x.SalesInvoiceItemKey)
                .Concat(shares.Select(x => x.SalesInvoiceItemKey))
                .ToList();

            // Atualiza no lugar a linha que continua, em vez de apagar e reinserir: o índice único
            // (ticket, linha) não pode ver a mesma linha duas vezes no mesmo SaveChanges.
            var incoming = shares.ToDictionary(x => x.SalesInvoiceItemKey);

            foreach (var existing in discharge.Items.ToList())
            {
                if (incoming.Remove(existing.SalesInvoiceItemKey, out var replacement))
                {
                    existing.Quantity = replacement.Quantity;
                    continue;
                }

                discharge.Items.Remove(existing);
                context.ShipmentLoadsDischargesItems.Remove(existing);
            }

            foreach (var added in incoming.Values)
                discharge.Items.Add(added);

            discharge.TicketNumber = ticket;
            discharge.DischargeDate = dischargeDate.Date;
            discharge.DischargedQuantity = total;
            discharge.Comments = comments;
            discharge.UpdatedAt = DateTime.Now;
            discharge.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Discharge,
                before,
                ShipmentLoadChangeLogFields.DescribeDischarge(
                    ticket, total, ShipmentLoadDischargesCreateService.Describe(discharge.Items)),
                userName);

            await recalculate.RecalculateAsync(load.Key, touchedItemKeys);

            await closureHook.ApplyAsync(load.Key, userName);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
