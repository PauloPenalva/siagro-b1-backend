using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.MenuItem;

public class MenuItemsDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<MenuItemsDeleteService> logger)
{
    public async Task ExecuteAsync(Guid id)
    {
        var entity = await db.MenuItems.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(resource["MENU_ITEM_NOT_FOUND"]);
        
        db.MenuItems.Remove(entity);
        await db.SaveChangesAsync();
    }
}