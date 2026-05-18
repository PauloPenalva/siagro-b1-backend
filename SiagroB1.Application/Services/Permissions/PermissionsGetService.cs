using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Permissions;

public class PermissionsGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<PermissionsGetService> logger)
{
    public async Task<Permission?> GetByIdAsync(string name)
    {
        try
        {
            return await db.Permissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<Permission> QueryAll()
    {
        return db.Permissions.AsNoTracking();
    }
}