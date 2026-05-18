using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.UsersProfiles;

public class UsersProfilesGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<UsersProfilesGetService> logger)
{
    public async Task<UserProfile?> GetByIdAsync(Guid userId, Guid userProfileId)
    {
        try
        {
            return await db.UserProfiles
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Id == userProfileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<UserProfile> QueryAll(Guid userId)
    {
        return db.UserProfiles
            .Where(p => p.UserId == userId)
            .AsNoTracking();
    }
}