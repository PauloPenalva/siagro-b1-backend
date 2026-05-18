using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ProfilesRoles;

public class ProfilesRolesDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<ProfilesRolesDeleteService> logger)
{
    public async Task ExecuteAsync(string profileCode, Guid profileRoleId)
    {
        var profileRole = await db.ProfileRoles.FirstOrDefaultAsync(
            p => p.ProfileCode == profileCode && p.Id == profileRoleId)
            ?? throw new NotFoundException(resource["PROFILE_ROLE_NOT_FOUND"].Value);
        
        try
        {
            db.ProfileRoles.Remove(profileRole);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }    
}