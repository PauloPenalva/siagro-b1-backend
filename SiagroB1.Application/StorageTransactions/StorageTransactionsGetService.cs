using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsGetService(AppDbContext context, ILogger<StorageTransactionsGetService> logger)
{
    public async Task<StorageTransaction?> GetByIdAsync(Guid key)
    {
        try
        {
            return await context.StorageTransactions
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<StorageTransaction> QueryAll()
    {
        return context.StorageTransactions.AsNoTracking();
    }
}