using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesPauseService(AppDbContext context, ILogger<ShipmentReleasesCancelationService> logger)
{
    public async Task ExecuteAsync(Guid key)
    {
        var sr = await context.ShipmentReleases
                     .Include(x => x.Transactions)
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                 throw new NotFoundException($"Shipment Release not found key {key}");
        
        if (sr.Status is ReleaseStatus.Cancelled or ReleaseStatus.Completed or ReleaseStatus.Paused)
        {
            throw new ApplicationException("Shipment Release is not in Activated state.");
        }
        
        sr.Status = ReleaseStatus.Paused;
        sr.UpdatedBy = string.Empty;
        sr.UpdatedAt = DateTime.Now;
        
        await context.SaveChangesAsync();
    }
}