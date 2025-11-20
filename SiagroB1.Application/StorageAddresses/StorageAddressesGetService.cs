using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesGetService(AppDbContext context, ILogger<StorageAddressesGetService> logger)
{
    public async Task<StorageAddress?> GetByIdAsync(Guid key)
    {
        try
        {
            return await context.StorageAddresses
                .Include(x => x.Transactions)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<StorageAddress> QueryAll()
    {
        return context.StorageAddresses.AsNoTracking();
    }
}