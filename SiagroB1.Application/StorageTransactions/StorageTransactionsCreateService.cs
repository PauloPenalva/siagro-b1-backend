using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCreateService(AppDbContext context, ILogger<StorageTransactionsCreateService> logger)
{
    public async Task<StorageTransaction> ExecuteAsync(
        StorageTransaction entity, string userName, 
        TransactionCode transactionCode = TransactionCode.StorageTransaction)
    {
        try
        {
            entity.TransactionOrigin = transactionCode;
            await context.StorageTransactions.AddAsync(entity);
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