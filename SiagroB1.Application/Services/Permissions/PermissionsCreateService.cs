using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Permissions;

public class PermissionsCreateService(CommonDbContext db, ILogger<PermissionsCreateService> logger)
{
    public async Task<Permission> ExecuteAsync(Permission entity)
    {
        try
        {
            await db.Permissions.AddAsync(entity);
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