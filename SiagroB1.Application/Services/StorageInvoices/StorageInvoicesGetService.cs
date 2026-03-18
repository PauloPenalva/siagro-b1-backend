using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageInvoices;

public class StorageInvoicesGetService(IUnitOfWork db, ILogger<StorageInvoicesGetService> logger)
{
    public async Task<StorageInvoice?> GetByIdAsync(Guid key)
    {
        try
        {
            return await db.Context.StorageInvoices
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<StorageInvoice> QueryAll()
    {
        return db.Context.StorageInvoices
            .Include(x => x.Items)
            .AsNoTracking();
    }
}