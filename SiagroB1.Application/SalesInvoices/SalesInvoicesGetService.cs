using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesGetService(IUnitOfWork db, ILogger<SalesInvoicesGetService> logger)
{
    public async Task<SalesInvoice?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await db.Context.SalesInvoices
                .Include(x => x.Items)
                .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<SalesInvoice> QueryAll()
    {
        return db.Context.SalesInvoices.AsNoTracking();
    }
}