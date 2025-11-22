using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShippingOrders;

public class ShippingOrdersUpdateService(AppDbContext context, ILogger<ShippingOrdersUpdateService> logger)
{
    public async Task<ShippingOrder?> ExecuteAsync(Guid key, ShippingOrder entity, string userName)
    {
        var existingEntity = await context.Set<ShippingOrder>()
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? 
                             throw new KeyNotFoundException("Entity not found.");
        
        try
        {
            context.Entry(existingEntity).CurrentValues.SetValues(entity);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}