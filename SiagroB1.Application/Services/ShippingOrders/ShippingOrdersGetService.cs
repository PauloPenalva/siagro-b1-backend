using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShippingOrders;

public class ShippingOrdersGetService(AppDbContext context, ILogger<ShippingOrdersUpdateService> logger)
{
    public async Task<ShippingOrder?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await context.ShippingOrders
                .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<ShippingOrder> QueryAll()
    {
        return context.ShippingOrders.AsNoTracking();
    }
}