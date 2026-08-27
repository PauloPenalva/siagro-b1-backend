using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesGetService(IUnitOfWork db, ILogger<SalesShipmentReleasesGetService> logger)
{
    public async Task<SalesShipmentRelease?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await db.Context.SalesShipmentReleases
                .Include(x => x.SalesContract)
                .Include(x => x.Branch)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    /// <summary>
    /// Espelho de <c>ShipmentReleasesGetService.QueryAll</c>: <b>sem
    /// <c>Include(Transactions)</c></b>, pelo mesmo motivo (Include incondicional
    /// estourando o CommandTimeout na carga inicial, sem ninguém consumir a coleção — o
    /// Detail usa binding próprio em <c>/StorageTransactions</c>).
    /// </summary>
    public IQueryable<SalesShipmentRelease> QueryAll()
    {
        return db.Context.SalesShipmentReleases
            .AsNoTracking();
    }
}
