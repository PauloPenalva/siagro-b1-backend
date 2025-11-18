using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesUpdateService(AppDbContext context, ILogger<ShipmentReleasesUpdateService> logger)
{
    public async Task<ShipmentRelease?> ExecuteAsync(Guid key, ShipmentRelease entity)
    {
        try
        {
            var existingEntity = await context.ShipmentReleases
                .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
            context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Save changes
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(key))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                logger.Log(LogLevel.Error, "Failed to update entity.");
                throw new DefaultException("Error updating entity due to concurrency issues.");
            }
        }

        return entity;
    }

    private bool EntityExists(Guid key)
    {
        return context.ShipmentReleases.Any(e => e.Key == key);
    }
}