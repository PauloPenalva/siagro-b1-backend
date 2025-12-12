using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsQualityInspectionsDeleteService(
    AppDbContext context, ILogger<StorageTransactionsQualityInspectionsDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid associationKey)
    {
        try
        {
            var existingEntity = await context.StorageTransactionQualityInspections.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Quality inspection not found");

            context.StorageTransactionQualityInspections.Remove(existingEntity);
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
            if (!ExistingStorageTransaction(parentKey))
            {
                throw new NotFoundException("Storage Transaction not found");
            }
            
            var existingEntity = await context.StorageTransactionQualityInspections.FindAsync(associationKey)
                ?? throw new NotFoundException("Quality inspection not found");

            context.StorageTransactionQualityInspections.Remove(existingEntity);
            await context.SaveChangesAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }

    private bool ExistingStorageTransaction(Guid key)
    {
        return context.StorageTransactions.Any(x => x.Key == key);
    }
}