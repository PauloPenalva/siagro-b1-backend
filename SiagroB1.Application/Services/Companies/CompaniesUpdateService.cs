using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

public class CompaniesUpdateService(
    CommonDbContext db,
    ICompanyDatabaseProbe probe,
    ICompanyCatalog catalog,
    ILogger<CompaniesUpdateService> logger)
{
    public async Task<Company?> ExecuteAsync(string code, Company entity)
    {
        var existing = await db.Companies.FirstOrDefaultAsync(c => c.Code == code)
                       ?? throw new NotFoundException("Empresa não encontrada.");

        var name = (entity.CompanyName ?? string.Empty).Trim();
        if (name.Length == 0) throw new DefaultException("Informe o nome da empresa.");

        // Senha mascarada = o admin não mexeu nela: mantém a armazenada.
        var connectionString = CompanyConnectionStrings.MergeMaskedPassword(entity.ConnectionString ?? string.Empty, existing.ConnectionString);

        // Equivalência pelo builder: o merge devolve a forma normalizada, que textualmente sempre "mudaria".
        if (CompanyConnectionStrings.AreEquivalent(connectionString, existing.ConnectionString))
            connectionString = existing.ConnectionString;
        else
            await CompanyDatabaseRules.ValidateConnectionStringAsync(connectionString, probe, commonConnectionString: null);

        // Code é imutável: é a chave que sessões, perfis e jobs carregam.
        existing.CompanyName = name;
        existing.ConnectionString = connectionString;
        existing.SapCompanyDb = entity.SapCompanyDb;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        catalog.Invalidate();
        return existing;
    }
}
