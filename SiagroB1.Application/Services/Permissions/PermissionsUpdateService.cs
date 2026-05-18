using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Permissions;

public class PermissionsUpdateService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<PermissionsUpdateService> logger)
{
    public async Task<Role?> ExecuteAsync(string name, Role entity)
    {
        var existingEntity = await db.Permissions
            .FirstOrDefaultAsync(x => x.Code == name) ?? 
                             throw new NotFoundException(resource["PERMISSION_NOT_FOUND"].Value);
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