using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsQualityInspectionsCreateService(
    AppDbContext context, ILogger<StorageTransactionsQualityInspectionsCreateService> logger)
{
    public async Task<StorageTransactionQualityInspection> ExecuteAsync(Guid storageTransactionKey, StorageTransactionQualityInspection associationEntity)
    {
        try
        {
            var existingEntity = await context.StorageTransactions.FindAsync(storageTransactionKey)
                ?? throw new NotFoundException("Storage transaction not found");
            
            associationEntity.StorageTransaction = existingEntity;
            await context.AddAsync(associationEntity);
            
            await context.SaveChangesAsync();
            
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}