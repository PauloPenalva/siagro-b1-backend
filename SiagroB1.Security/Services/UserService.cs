using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Shared;

namespace SiagroB1.Security.Services;

public class UserService(CommonDbContext db, ILogger<UserService> logger)
{
    public async Task<dynamic> CreateAdminUserAsync()
    {
        var existsAdminUser = db.Users.Any(x => x.Username.Equals("admin"));
        if (existsAdminUser)
        {
            throw new ApplicationException("Admin user already exists.");    
        }
        
        var user = new User
        {
            Username = "admin",
            PasswordHash = Utils.HashPassword("1234"),  // Use BCrypt em produção!
            FullName = "Administrator",
            Email = "admin@siagrob1.com",
            IsAdmin = true,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        logger.LogInformation("User created: {Username}", user.Username);

        return new 
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            IsAdmin = user.IsAdmin
        };
    }
    
}