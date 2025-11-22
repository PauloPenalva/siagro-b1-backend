using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShippingOrders;

public class ShippingOrdersDeleteService(AppDbContext context, ILogger<ShippingOrdersDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            await context.SaveChangesAsync();
        });
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid key, Func<ShippingOrder, Task>? preDeleteAction = null)
    {
        var entity = await context.ShippingOrders
            .FirstOrDefaultAsync(x => x.Key == key) ??
                throw new NotFoundException("Shipping order not found.");
        
        if (entity.Status != ShippingOrderStatus.Planned)
        {
            throw new ApplicationException("Shipping order must be in planned status.");
        }

        
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            if (preDeleteAction != null)
                await preDeleteAction(entity);

            context.ShippingOrders.Remove(entity);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(ShippingOrder), key);
            throw;
        }
    }
}