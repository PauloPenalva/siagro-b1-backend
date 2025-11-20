using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesCreateService(AppDbContext context, ILogger<ShipmentReleasesCreateService> logger)
{
    public async Task<ShipmentRelease> ExecuteAsync(ShipmentRelease entity, string userName)
    {
        try
        {
            entity.Status = ReleaseStatus.Pending;
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = userName;
            await context.ShipmentReleases.AddAsync(entity);
            await context.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                throw;
            }

            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }    
}