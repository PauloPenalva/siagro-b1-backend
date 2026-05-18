using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.RolesMenus;

public class RolesMenusDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<RolesMenusDeleteService> logger)
{
    public async Task ExecuteAsync(string roleCode, Guid roleMenuId)
    {
        var menus = await db.RolesMenus.FirstOrDefaultAsync(
            p => p.RoleCode == roleCode && p.Id == roleMenuId)
            ?? throw new NotFoundException(resource["ROLE_MENU_NOT_FOUND"].Value);
        
        try
        {
            db.RolesMenus.Remove(menus);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }    
}