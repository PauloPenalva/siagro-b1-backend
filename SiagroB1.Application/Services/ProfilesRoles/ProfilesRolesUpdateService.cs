using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ProfilesRoles;

public class ProfilesRolesUpdateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<ProfilesRolesUpdateService> logger)
{
    public async Task<ProfileRole> ExecuteAsync(string profileCode, Guid profileRoleId, ProfileRole entity)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Code == profileCode);
        if (profile == null)
            throw new NotFoundException(resource["PROFILE_NOT_FOUND"].Value);
       
        var profileRole = await db.ProfileRoles.FirstOrDefaultAsync(p => p.Id == profileRoleId);
        if (profileRole == null)
            throw new NotFoundException(resource["PROFILE_ROLE_NOT_FOUND"].Value);
        
        profileRole.ProfileCode = profileCode;
        profileRole.RoleCode = entity.RoleCode;
        
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