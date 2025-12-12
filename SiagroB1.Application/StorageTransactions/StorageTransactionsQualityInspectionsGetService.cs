using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsQualityInspectionsGetService(AppDbContext context, ILogger<StorageTransactionsQualityInspectionsGetService> logger)
{
    
    public async Task<StorageTransactionQualityInspection?> GetByIdAsync(Guid key)
    {
        try
        {
            return await context.StorageTransactionQualityInspections.FindAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public async Task<StorageTransactionQualityInspection?> GetByIdAsync(Guid key, Guid associationKey)
    {
        try
        {
            if (!ExistStorageTransaction(key))
            {
                throw new NotFoundException("Storage transaction key not found.");
            }
            
            return await context.StorageTransactionQualityInspections.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<StorageTransactionQualityInspection> QueryAll(Guid parentKey)
    {
        return context.StorageTransactionQualityInspections
            .Where(x => x.StorageTransactionKey == parentKey)
            .AsNoTracking();
    }

    private bool ExistStorageTransaction(Guid key)
    {
        return context.StorageTransactions.Any(x => x.Key == key);
    }
}