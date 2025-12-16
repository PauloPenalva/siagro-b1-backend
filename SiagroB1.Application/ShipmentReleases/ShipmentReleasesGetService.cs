using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesGetService(IUnitOfWork db, ILogger<ShipmentReleasesGetService> logger)
{
    public async Task<ShipmentRelease?> GetByIdAsync(int rowId)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", rowId);
            return await db.Context.ShipmentReleases
                .Include(x => x.PurchaseContract)
                .FirstOrDefaultAsync(x => x.RowId == rowId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", rowId);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<ShipmentRelease> QueryAll()
    {
        return db.Context.ShipmentReleases.AsNoTracking();
    }
}