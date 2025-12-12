using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsQualityInspectionsUpdateService(
    AppDbContext context, ILogger<StorageTransactionsQualityInspectionsUpdateService> logger)
{
    public async Task<StorageTransactionQualityInspection?> ExecuteAsync(Guid associationKey, StorageTransactionQualityInspection associationEntity)
    {
        try
        {
            var existingEntity = await context.StorageTransactionQualityInspections.FindAsync(associationKey)
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
    
    public async Task<StorageTransactionQualityInspection?> ExecuteAsync(Guid parenteKey, Guid associationKey, StorageTransactionQualityInspection associationEntity)
    {
        try
        {
            if (!ExistingStorageTransaction(parenteKey))
            {
                throw new NotFoundException("Storage Transaction not found.");
            }
            
            var existingEntity = await context.StorageTransactionQualityInspections.FindAsync(associationKey)
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

    private bool ExistingStorageTransaction(Guid key)
    {
        return  context.StorageTransactions.Any(x => x.Key == key);
    }
}