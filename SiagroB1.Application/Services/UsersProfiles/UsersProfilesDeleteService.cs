using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.UsersProfiles;

public class UsersProfilesDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<UsersProfilesDeleteService> logger)
{
    public async Task ExecuteAsync(Guid userId, Guid userProfileId)
    {
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(
            p => p.UserId == userId && p.Id == userProfileId)
            ?? throw new NotFoundException(resource["PROFILE_ROLE_NOT_FOUND"].Value);
        
        try
        {
            db.UserProfiles.Remove(userProfile);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }    
}