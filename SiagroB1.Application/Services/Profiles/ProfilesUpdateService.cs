using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Profiles;

public class ProfilesUpdateService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<ProfilesUpdateService> logger)
{
    public async Task<Profile?> ExecuteAsync(string code, Profile entity)
    {
        var existingEntity = await db.Profiles
            .FirstOrDefaultAsync(x => x.Code == code) ?? 
                             throw new NotFoundException(resource["PROFILE_NOT_FOUND"].Value);
        try
        {
            db.Entry(existingEntity).CurrentValues.SetValues(entity);

            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}