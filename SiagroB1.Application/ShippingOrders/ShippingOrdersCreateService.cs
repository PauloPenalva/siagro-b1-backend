using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShippingOrders;

public class ShippingOrdersCreateService(AppDbContext context, ILogger<ShippingOrdersCreateService> logger)
{
    public async Task<ShippingOrder> ExecuteAsync(ShippingOrder entity, string createdBy)
    {
        try
        {
            entity.Status = ShippingOrderStatus.Planned;
            await context.ShippingOrders.AddAsync(entity);
            await context.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException("Unable to shipping order contract.");
        }
    }    
}