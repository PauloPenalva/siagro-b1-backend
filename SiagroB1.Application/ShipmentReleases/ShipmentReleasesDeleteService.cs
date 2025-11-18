using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesDeleteService(AppDbContext context, ILogger<ShipmentReleasesDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            await context.SaveChangesAsync();
        });
    }

    private bool EntityExists(Guid key)
    {
        return context.Set<PurchaseContract>().Any(e => e.Key == key);
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid id, Func<ShipmentRelease, Task>? preDeleteAction = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var entity = await context.ShipmentReleases.FindAsync(id);
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(ShipmentRelease), id);
                return false;
            }

            if (preDeleteAction != null)
                await preDeleteAction(entity);

            context.ShipmentReleases.Remove(entity);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(ShipmentRelease), id);
            throw;
        }
    }
}