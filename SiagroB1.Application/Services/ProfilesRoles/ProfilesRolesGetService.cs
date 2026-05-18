using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ProfilesRoles;

public class ProfilesRolesGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<ProfilesRolesGetService> logger)
{
    public async Task<ProfileRole?> GetByIdAsync(string profileCode, Guid profileRoleId)
    {
        try
        {
            return await db.ProfileRoles
                .FirstOrDefaultAsync(p => p.ProfileCode == profileCode && p.Id == profileRoleId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<ProfileRole> QueryAll(string profileCode)
    {
        return db.ProfileRoles
            .Where(p => p.ProfileCode == profileCode)
            .AsNoTracking();
    }
}