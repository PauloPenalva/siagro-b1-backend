using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.RolesPermissions;

public class RolesPermissionsDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<RolesPermissionsDeleteService> logger)
{
    public async Task ExecuteAsync(string roleCode, Guid rolePermissionId)
    {
        var permission = await db.RolesPermissions.FirstOrDefaultAsync(
            p => p.RoleCode == roleCode && p.Id == rolePermissionId)
            ?? throw new NotFoundException(resource["ROLE_PERMISSION_NOT_FOUND"].Value);
        
        try
        {
            db.RolesPermissions.Remove(permission);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }    
}