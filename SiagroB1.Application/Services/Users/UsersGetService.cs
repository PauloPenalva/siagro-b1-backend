using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Users;

public class UsersGetService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<UsersGetService> logger)
{
    public async Task<User?> GetByIdAsync(Guid id)
    {
        try
        {
            return await db.Users
                .FirstOrDefaultAsync(p => p.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, resource["ERROR_FETCHING_ENTITY"].Value);
            throw new DefaultException(resource["ERROR_FETCHING_ENTITY"].Value);
        }
    }

    public IQueryable<User> QueryAll()
    {
        return db.Users.AsNoTracking();
    }
}