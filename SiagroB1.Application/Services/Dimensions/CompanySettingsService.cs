using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>Parâmetros da empresa: linha única (Id = 1), criada sob demanda se faltar.</summary>
public class CompanySettingsService(IUnitOfWork db)
{
    public async Task<CompanySettings> GetAsync()
    {
        var settings = await db.Context.CompanySettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (settings is not null)
        {
            return settings;
        }

        settings = new CompanySettings { Id = 1 };
        db.Context.CompanySettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    /// <summary>
    /// Leitura SEM escrita, para a guarda: linha ausente = obrigatoriedades desligadas. Nunca chama
    /// SaveChanges, para não gravar no meio de uma confirmação as mudanças rastreadas de quem chama.
    /// </summary>
    public async Task<CompanySettings> GetReadOnlyAsync()
    {
        return await db.Context.CompanySettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1)
               ?? new CompanySettings { Id = 1 };
    }

    public async Task<CompanySettings> UpdateAsync(bool requireCostCenter, bool requireLedgerAccount)
    {
        var settings = await GetAsync();
        settings.RequireCostCenter = requireCostCenter;
        settings.RequireLedgerAccount = requireLedgerAccount;
        await db.SaveChangesAsync();
        return settings;
    }
}
