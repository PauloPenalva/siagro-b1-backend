using Microsoft.Extensions.Logging;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.MenuItem;

public class MenuItemsCreateService(CommonDbContext db, ILogger<MenuItemsCreateService> logger)
{
    public async Task<Domain.Entities.Common.MenuItem> ExecuteAsync(Domain.Entities.Common.MenuItem entity)
    {
        try
        {
            await db.MenuItems.AddAsync(entity);
            await db.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }    
}