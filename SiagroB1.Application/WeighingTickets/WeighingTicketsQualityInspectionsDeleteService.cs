using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsQualityInspectionsDeleteService(
    AppDbContext context, ILogger<WeighingTicketsQualityInspectionsDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid associationKey)
    {
        try
        {
            var existingEntity = await context.QualityInspections.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Quality inspection not found");

            context.QualityInspections.Remove(existingEntity);
            await context.SaveChangesAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }
    
    public async Task<bool> ExecuteAsync(Guid parentKey, Guid associationKey)
    {
        try
        {
            if (!ExistingQualityInspection(parentKey))
            {
                throw new NotFoundException("Quality inspection not found");
            }
            
            var existingEntity = await context.QualityInspections.FindAsync(associationKey)
                ?? throw new NotFoundException("Quality inspection not found");

            context.QualityInspections.Remove(existingEntity);
            await context.SaveChangesAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }

    private bool ExistingQualityInspection(Guid key)
    {
        return context.QualityInspections.Any(x => x.Key == key);
    }
}