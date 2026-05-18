using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Roles;

public class RolesDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<RolesDeleteService> logger)
{
    public async Task ExecuteAsync(string code)
    {
        var entity = await db.Roles.FirstOrDefaultAsync(c => c.Code == code)
            ?? throw new NotFoundException(resource["ROLE_NOT_FOUND"].Value);
        
        db.Roles.Remove(entity);
        await db.SaveChangesAsync();
    }
}