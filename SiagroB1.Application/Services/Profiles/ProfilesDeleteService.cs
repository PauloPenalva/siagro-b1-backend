using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Profiles;

public class ProfilesDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<ProfilesDeleteService> logger)
{
    public async Task ExecuteAsync(Guid id)
    {
        var entity = await db.Profiles.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(resource["PROFILE_NOT_FOUND"].Value);
        
        db.Profiles.Remove(entity);
        await db.SaveChangesAsync();
    }
}