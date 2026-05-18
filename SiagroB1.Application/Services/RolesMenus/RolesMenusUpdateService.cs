using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.RolesMenus;

public class RolesMenusUpdateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<RolesMenusUpdateService> logger)
{
    public async Task<RoleMenu> ExecuteAsync(string roleCode, Guid roleMenuId, RoleMenu entity)
    {
        var role = await db.Roles.FirstOrDefaultAsync(p => p.Code == roleCode);
        if (role == null)
            throw new NotFoundException(resource["ROLE_NOT_FOUND"].Value);
       
        var roleMenu = await db.RolesMenus.FirstOrDefaultAsync(p => p.Id == roleMenuId);
        if (roleMenu == null)
            throw new NotFoundException(resource["ROLE_MENU_NOT_FOUND"].Value);
        
        roleMenu.RoleCode = roleCode;
        roleMenu.MenuItemId = entity.MenuItemId;
        
        try
        {
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