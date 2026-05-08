using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Users;

public class UsersDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<UsersDeleteService> logger)
{
    public async Task ExecuteAsync(Guid id)
    {
        var entity = await db.Users.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(resource["MENU_ITEM_NOT_FOUND"]);
        
        db.Users.Remove(entity);
        await db.SaveChangesAsync();
    }
}