using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.MenuItem;

public class MenuItemsUpdateService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<MenuItemsUpdateService> logger)
{
    public async Task<Domain.Entities.Common.MenuItem?> ExecuteAsync(Guid id, Domain.Entities.Common.MenuItem entity)
    {
        var existingEntity = await db.MenuItems
            .FirstOrDefaultAsync(x => x.Id == id) ?? 
                             throw new NotFoundException(resource["MENU_ITEM_NOT_FOUND"].Value);
        try
        {
            db.Entry(existingEntity).CurrentValues.SetValues(entity);

            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}