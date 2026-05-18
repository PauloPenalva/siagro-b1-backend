using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Roles;

public class RolesCreateService(CommonDbContext db, ILogger<RolesCreateService> logger)
{
    public async Task<Role> ExecuteAsync(Role entity)
    {
        try
        {
            await db.Roles.AddAsync(entity);
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