using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Permissions;

public class PermissionsDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<PermissionsDeleteService> logger)
{
    public async Task ExecuteAsync(string name)
    {
        var entity = await db.Permissions.FirstOrDefaultAsync(c => c.Code == name)
            ?? throw new NotFoundException(resource["PERMISSION_NOT_FOUND"].Value);
        
        db.Permissions.Remove(entity);
        await db.SaveChangesAsync();
    }
}