using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesDeleteService(IUnitOfWork db, ILogger<SalesInvoicesDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            await db.Context.Entry(entity).Collection(e => e.Items).LoadAsync();
            if (entity.Items.Any())
                db.Context.SalesInvoicesItems.RemoveRange(entity.Items);
            
            await db.SaveChangesAsync();
        });
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid key, Func<SalesInvoice, Task>? preDeleteAction = null)
    {
        try
        {
            await db.BeginTransactionAsync();
            var entity = await db.Context.SalesInvoices
                .FirstOrDefaultAsync(x => x.Key == key);
            
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(SalesContract), key);
                return false;
            }

            
            if (preDeleteAction != null)
                await preDeleteAction(entity);

            db.Context.SalesInvoices.Remove(entity);
            
            await db.SaveChangesAsync();
            await db.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(SalesContract), key);
            throw;
        }
    }
}