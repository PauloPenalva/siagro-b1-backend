using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsDeleteService(AppDbContext context, ILogger<StorageTransactionsDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key, TransactionCode transactionCode = TransactionCode.StorageTransaction)
    {
        var entity = await context.StorageTransactions
            .FirstOrDefaultAsync(x => x.Key == key) ?? 
                     throw new NotFoundException("Storage Transaction not found.");

        if (entity.TransactionOrigin != transactionCode)
        {
            throw new ApplicationException("This record was created by another transaction. It cannot be deleted.");
        }

        if (entity.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            throw new ApplicationException("Storage transaction must be in waiting status.");
        }
        
        context.StorageTransactions.Remove(entity);
        await context.SaveChangesAsync();
        return true;
    }
}