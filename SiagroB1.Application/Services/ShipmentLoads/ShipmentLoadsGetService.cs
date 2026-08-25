using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

public class ShipmentLoadsGetService(IUnitOfWork db, ILogger<ShipmentLoadsGetService> logger)
{
    public async Task<ShipmentLoad?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await db.Context.ShipmentLoads
                .Include(x => x.Branch)
                .Include(x => x.Transactions)
                .Include(x => x.Invoices)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<ShipmentLoad> QueryAll()
    {
        return db.Context.ShipmentLoads.AsNoTracking();
    }

    public IQueryable<ShipmentLoadMovement> QueryMovements()
    {
        return db.Context.ShipmentLoadMovements.AsNoTracking();
    }
}
