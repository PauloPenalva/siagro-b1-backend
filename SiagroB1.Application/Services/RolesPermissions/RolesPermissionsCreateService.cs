using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.RolesPermissions;

public class RolesPermissionsCreateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<RolesPermissionsCreateService> logger)
{
    public async Task<RolePermission> ExecuteAsync(string roleCode, RolePermission entity)
    {
        var role = await db.Roles.FirstOrDefaultAsync(p => p.Code == roleCode);
        if (role == null)
            throw new NotFoundException(resource["ROLE_NOT_FOUND"].Value);
       
        entity.RoleCode = roleCode;
        
        try
        {
            await db.RolesPermissions.AddAsync(entity);
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