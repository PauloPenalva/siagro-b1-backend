using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.RolesPermissions;

public class RolesPermissionsUpdateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<RolesPermissionsUpdateService> logger)
{
    public async Task<RolePermission> ExecuteAsync(string roleCode, Guid rolePermissionId, RolePermission entity)
    {
        var role = await db.Roles.FirstOrDefaultAsync(p => p.Code == roleCode);
        if (role == null)
            throw new NotFoundException(resource["ROLE_NOT_FOUND"].Value);
       
        var rolePermission = await db.RolesPermissions.FirstOrDefaultAsync(p => p.Id == rolePermissionId);
        if (rolePermission == null)
            throw new NotFoundException(resource["ROLE_PERMISSION_NOT_FOUND"].Value);
        
        rolePermission.RoleCode = roleCode;
        rolePermission.PermissionCode = entity.PermissionCode;
        
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