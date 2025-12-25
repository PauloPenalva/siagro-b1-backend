using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsQualityInspectionsUpdateService(
    AppDbContext context, ILogger<WeighingTicketsQualityInspectionsUpdateService> logger)
{
    public async Task<QualityInspection?> ExecuteAsync(Guid associationKey, QualityInspection associationEntity)
    {
        try
        {
            var existingEntity = await context.QualityInspections.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Quality inspection not found");

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);
            await context.SaveChangesAsync();
            
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
    
    public async Task<QualityInspection?> ExecuteAsync(Guid parenteKey, Guid associationKey, QualityInspection associationEntity)
    {
        try
        {
            if (!ExistingQualityInspection(parenteKey))
            {
                throw new NotFoundException("Quality inspection not found.");
            }
            
            var existingEntity = await context.QualityInspections.FindAsync(associationKey)
                ?? throw new NotFoundException("Quality inspection not found.");

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);
            await context.SaveChangesAsync();
            
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    private bool ExistingQualityInspection(Guid key)
    {
        return  context.QualityInspections.Any(x => x.Key == key);
    }
}