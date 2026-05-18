using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.RolesPermissions;

public class RolesPermissionsGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<RolesPermissionsGetService> logger)
{
    public async Task<RolePermission?> GetByIdAsync(string roleCode, Guid rolePermissionId)
    {
        try
        {
            return await db.RolesPermissions
                .FirstOrDefaultAsync(p => p.RoleCode == roleCode && p.Id == rolePermissionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<RolePermission> QueryAll(string roleCode)
    {
        return db.RolesPermissions
            .Where(p => p.RoleCode == roleCode)
            .AsNoTracking();
    }
}