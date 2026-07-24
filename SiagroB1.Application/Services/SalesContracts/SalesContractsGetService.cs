using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsGetService(AppDbContext context, ILogger<SalesContractsUpdateService> logger)
{
    public async Task<SalesContract?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await context.SalesContracts
                .Include(x => x.DocNumber)
                // Filial do contrato. Sem o Include o $expand=Branch da tela devolve nulo:
                // o EnableQuery projeta sobre a entidade já materializada, não sobre a query.
                .Include(x => x.Branch)
                .Include(x => x.HarvestSeason)
                .Include(x => x.LogisticRegion)
                .Include(x => x.SalesInvoiceItems)
                .ThenInclude(x => x.SalesInvoice)
                // Necessário para a seção "Liberações de Entrega" do Detail ($expand
                // serializa a partir da nav carregada) e para os computed
                // TotalShipmentReleases/TotalAvailableToRelease no header.
                .Include(x => x.SalesShipmentReleases)
                .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<SalesContract> QueryAll()
    {
        return context.SalesContracts
            .Include(x => x.SalesInvoiceItems)
            .ThenInclude(x => x.SalesInvoice)
            .AsNoTracking();
    }
}