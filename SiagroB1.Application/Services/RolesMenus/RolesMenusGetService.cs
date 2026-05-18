using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.RolesMenus;

public class RolesMenusGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<RolesMenusGetService> logger)
{
    public async Task<RoleMenu?> GetByIdAsync(string roleCode, Guid roleMenuId)
    {
        try
        {
            return await db.RolesMenus
                .FirstOrDefaultAsync(p => p.RoleCode == roleCode && p.Id == roleMenuId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<RoleMenu> QueryAll(string roleCode)
    {
        return db.RolesMenus
            .Where(p => p.RoleCode == roleCode)
            .AsNoTracking();
    }
}