using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.UsersProfiles;

public class UsersProfilesCreateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<UsersProfilesCreateService> logger)
{
    public async Task<UserProfile> ExecuteAsync(Guid userId, UserProfile entity)
    {
        var user = await db.Users.FirstOrDefaultAsync(p => p.Id == userId);
        if (user == null)
            throw new NotFoundException(resource["USER_NOT_FOUND"].Value);
       
        entity.UserId = user.Id;
        
        try
        {
            await db.UserProfiles.AddAsync(entity);
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