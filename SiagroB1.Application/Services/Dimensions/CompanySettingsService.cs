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

    public async Task<CompanySettings> UpdateAsync(bool requireCostCenter, bool requireLedgerAccount)
    {
        var settings = await GetAsync();
        settings.RequireCostCenter = requireCostCenter;
        settings.RequireLedgerAccount = requireLedgerAccount;
        await db.SaveChangesAsync();
        return settings;
    }
}
