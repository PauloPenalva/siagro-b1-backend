using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesGetService(IUnitOfWork db, ILogger<StorageAddressesGetService> logger)
{
    public async Task<StorageAddress?> GetByIdAsync(string code)
    {
        try
        {
            return await db.Context.StorageAddresses
                .Include(x => x.Transactions)
                .FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<StorageAddress> QueryAll()
    {
        return db.Context.StorageAddresses
            .Include(x => x.Transactions)
            .AsNoTracking();
    }
}