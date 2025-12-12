using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsUpdateService(AppDbContext context, ILogger<StorageTransactionsUpdateService> logger)
{
    public async Task<StorageTransaction?> ExecuteAsync(Guid key, StorageTransaction entity, string userName)
    {
        var existingEntity = await context.StorageTransactions
                                 .FirstOrDefaultAsync(tc => tc.Key == key) ?? 
                             throw new KeyNotFoundException("Entity not found.");

        if (existingEntity.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            throw new ApplicationException("Storage transaction must be in pending status.");
        }
        
        try
        {
            context.Entry(existingEntity).CurrentValues.SetValues(entity);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}