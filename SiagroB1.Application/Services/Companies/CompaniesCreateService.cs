using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

/// <summary>
/// Cadastrar uma empresa é cadastrar um BANCO. O banco tem de abrir, não pode ser a COMMON e tem de
/// estar com as migrations em dia — o Web se recusa a subir com migration pendente em qualquer
/// empresa cadastrada, então aceitar um banco atrasado derrubaria o próximo deploy.
/// Ordem operacional: criar o banco → dotnet ef database update --connection → cadastrar aqui.
/// </summary>
public partial class CompaniesCreateService(
    CommonDbContext db,
    ICompanyDatabaseProbe probe,
    ICompanyCatalog catalog,
    IConfiguration configuration,
    ILogger<CompaniesCreateService> logger)
{
    [GeneratedRegex("^[A-Z0-9]{1,4}$")]
    private static partial Regex CodePattern();

    public async Task<Company> ExecuteAsync(Company entity)
    {
        entity.Code = (entity.Code ?? string.Empty).Trim().ToUpperInvariant();
        entity.CompanyName = (entity.CompanyName ?? string.Empty).Trim();

        if (!CodePattern().IsMatch(entity.Code))
            throw new DefaultException("Código da empresa: de 1 a 4 letras ou dígitos.");

        if (entity.CompanyName.Length == 0)
            throw new DefaultException("Informe o nome da empresa.");

        if (await db.Companies.AnyAsync(c => c.Code == entity.Code))
            throw new DefaultException($"O código {entity.Code} já existe em outra empresa.");

        await CompanyDatabaseRules.ValidateConnectionStringAsync(
            entity.ConnectionString, probe, configuration.GetConnectionString("SiagroCommon"));

        try
        {
            await db.Companies.AddAsync(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }

        catalog.Invalidate();
        return entity;
    }
}
