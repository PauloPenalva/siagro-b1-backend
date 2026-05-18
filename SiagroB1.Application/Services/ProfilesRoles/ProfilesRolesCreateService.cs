using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ProfilesRoles;

public class ProfilesRolesCreateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<ProfilesRolesCreateService> logger)
{
    public async Task<ProfileRole> ExecuteAsync(string profileCode, ProfileRole entity)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Code == profileCode);
        if (profile == null)
            throw new NotFoundException(resource["PROFILE_NOT_FOUND"].Value);
       
        entity.ProfileCode = profileCode;
        
        try
        {
            await db.ProfileRoles.AddAsync(entity);
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