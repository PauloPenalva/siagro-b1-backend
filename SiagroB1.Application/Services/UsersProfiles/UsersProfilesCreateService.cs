using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.UsersProfiles;

public class UsersProfilesCreateService(
    CommonDbContext db,
    ICompanyContext companyContext,
    IStringLocalizer<Resource> resource,
    ILogger<UsersProfilesCreateService> logger)
{
    public async Task<UserProfile> ExecuteAsync(Guid userId, UserProfile entity)
    {
        var user = await db.Users.FirstOrDefaultAsync(p => p.Id == userId);
        if (user == null)
            throw new NotFoundException(resource["USER_NOT_FOUND"].Value);
       
        entity.UserId = user.Id;
        entity.CompanyCode = string.IsNullOrWhiteSpace(entity.CompanyCode)
            ? companyContext.CompanyCode
            : entity.CompanyCode.Trim().ToUpperInvariant();

        if (!await db.Companies.AnyAsync(c => c.Code == entity.CompanyCode))
            throw new DefaultException("Empresa inválida.");

        // O índice único (UserId, ProfileCode, CompanyCode) pega isto no SQL Server, mas a mensagem
        // de negócio precisa existir — e o InMemory dos testes não aplica índice nenhum.
        if (await db.UserProfiles.AnyAsync(p => p.UserId == user.Id && p.ProfileCode == entity.ProfileCode && p.CompanyCode == entity.CompanyCode))
            throw new DefaultException("Este perfil já está atribuído ao usuário nesta empresa.");

        try
        {
            await db.UserProfiles.AddAsync(entity);
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