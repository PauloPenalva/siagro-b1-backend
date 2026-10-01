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
    ShipmentLoadsClosureHookService closureHook,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesDeleteService> logger)
{
    public async Task ExecuteAsync(Guid dischargeKey, string userName)
    {
        try
        {
            var discharge = await context.ShipmentLoadsDischarges
                .Include(x => x.Items)
                .ThenInclude(x => x.SalesInvoice)
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            // Lidas ANTES do Remove: depois dele o recálculo não saberia quais linhas zerar.
            var itemKeys = discharge.Items.Select(x => x.SalesInvoiceItemKey).ToList();

            var before = ShipmentLoadChangeLogFields.DescribeDischarge(
                discharge.TicketNumber,
                discharge.DischargedQuantity,
                ShipmentLoadDischargesCreateService.Describe(discharge.Items).ToList());

            // Explícito, além do Cascade do banco: o EF InMemory dos testes não aplica FK.
            context.ShipmentLoadsDischargesItems.RemoveRange(discharge.Items);
            context.ShipmentLoadsDischarges.Remove(discharge);

            changeLog.Register(load.Key, ShipmentLoadChangeLogFields.Discharge, before, null, userName);

            await recalculate.RecalculateAsync(load.Key, itemKeys);

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
