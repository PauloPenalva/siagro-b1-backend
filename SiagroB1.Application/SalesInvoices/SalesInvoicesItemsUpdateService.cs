using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesItemsUpdateService(
    IUnitOfWork db, 
    IItemService itemService,
    ILogger<SalesInvoicesUpdateService> logger)
{
    public async Task<SalesInvoiceItem?> ExecuteAsync(Guid key, SalesInvoiceItem entity, string userName)
    {
        var existingEntity = await db.Context.SalesInvoicesItems
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
        
        try
        {
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            
            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}