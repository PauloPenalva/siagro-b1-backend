using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Profiles;

public class ProfilesGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<ProfilesGetService> logger)
{
    public async Task<Profile?> GetByIdAsync(string code)
    {
        try
        {
            return await db.Profiles
                .FirstOrDefaultAsync(p => p.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<Profile> QueryAll()
    {
        return db.Profiles.AsNoTracking();
    }
}