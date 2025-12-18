using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesDeleteService(IUnitOfWork db, ILogger<ShipmentReleasesDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var entity = await db.Context.ShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == key) ?? 
                     throw new NotFoundException("Shipment Release not found.");
        
        if (entity.Status != ReleaseStatus.Pending)
        {
            throw new ApplicationException("Shipment Release not pending.");
        }
        
        db.Context.ShipmentReleases.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }
}