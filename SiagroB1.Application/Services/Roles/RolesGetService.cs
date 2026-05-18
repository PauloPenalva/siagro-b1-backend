using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Roles;

public class RolesGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<RolesGetService> logger)
{
    public async Task<Role?> GetByIdAsync(string code)
    {
        try
        {
            return await db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<Role> QueryAll()
    {
        return db.Roles.AsNoTracking();
    }
}