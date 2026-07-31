using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Services.Users;

public class UsersCreateService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<UsersCreateService> logger)
{
    public async Task<UserDto> ExecuteAsync(User entity)
    {
        var existsUser = db.Users
            .Any(x => x.Username.Trim().ToUpper().Equals(entity.Username.Trim().ToUpper()));
        
        if (existsUser)
        {
            throw new ApplicationException(resource["USER_ALREADY_EXISTS"].Value);    
        }
        
        try
        {
            entity.PasswordHash = string.IsNullOrWhiteSpace(entity.Password)
                ? null                                        // Sem senha: só entra pelo "esqueci minha senha".
                : PasswordHasher.Hash(entity.Password);
            entity.Password = null;

            // E-mail é opcional (o OUSR do SAP também não o exige). O índice único de Email é
            // filtrado por IS NOT NULL: vários nulos convivem, mas duas strings vazias colidem -
            // e a tela manda "" quando o campo fica em branco.
            entity.Email = string.IsNullOrWhiteSpace(entity.Email) ? null : entity.Email.Trim();

            await db.Users.AddAsync(entity);
            await db.SaveChangesAsync();
            return new UserDto()
            {
                Id = entity.Id,
                Username = entity.Username,
                FullName = entity.FullName,
                Email = entity.Email,
                IsAdmin = entity.IsAdmin,
                IsActive = entity.IsActive
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }    
}