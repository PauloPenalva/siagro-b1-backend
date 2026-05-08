using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using Utils = SiagroB1.Security.Shared.Utils;

namespace SiagroB1.Application.Services.Users;

public class UsersUpdateService(
    CommonDbContext db,
    IStringLocalizer<Resource> resource,
    ILogger<UsersUpdateService> logger)
{
    public async Task<UserDto?> ExecuteAsync(Guid id, User user)
    {
        var existingUser = await db.Users
            .FirstOrDefaultAsync(x => x.Id == id) ?? 
                             throw new NotFoundException(resource["USER_NOT_FOUND"].Value);
        try
        {
            existingUser.IsActive = user.IsActive;
            existingUser.IsAdmin = user.IsAdmin;
            existingUser.Email = user.Email;
            existingUser.FullName = user.FullName!;
            
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update user.");
            throw new ApplicationException("Error updating user due to concurrency issues.");
        }

        return new UserDto()
        {
            Id = existingUser.Id,
            Username = existingUser.Username,
            FullName = existingUser.FullName,
            Email = existingUser.Email,
            IsAdmin = existingUser.IsAdmin,
            IsActive = existingUser.IsActive
        };
    }
    
}