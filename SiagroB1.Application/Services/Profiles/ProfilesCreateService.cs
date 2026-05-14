using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Profiles;

public class ProfilesCreateService(CommonDbContext db, ILogger<ProfilesCreateService> logger)
{
    public async Task<Profile> ExecuteAsync(Profile entity)
    {
        try
        {
            await db.Profiles.AddAsync(entity);
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