using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.OwnershipTransfers;

public class OwnershipTransfersGetService(IUnitOfWork db, ILogger<OwnershipTransfersGetService> logger)
{
    public async Task<OwnershipTransfer?> GetByIdAsync(Guid key)
    {
        try
        {
            return await db.Context.OwnershipTransfers
                 .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException(ex.Message);
        }
    }

    public IQueryable<OwnershipTransfer> QueryAll()
    {
        return db.Context.OwnershipTransfers
            .AsNoTracking();
    }
}