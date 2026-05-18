using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.UsersProfiles;

public class UsersProfilesUpdateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<UsersProfilesUpdateService> logger)
{
    public async Task<UserProfile> ExecuteAsync(Guid userId, Guid userProfileId, UserProfile entity)
    {
        var user = await db.Users.FirstOrDefaultAsync(p => p.Id == userId);
        if (user == null)
            throw new NotFoundException(resource["USER_NOT_FOUND"].Value);
       
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userProfileId);
        if (userProfile == null)
            throw new NotFoundException(resource["USER_PROFILE_NOT_FOUND"].Value);
        
        userProfile.UserId = userId;
        userProfile.ProfileCode = entity.ProfileCode;
        
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