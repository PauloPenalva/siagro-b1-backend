using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>Cadastro das alíquotas de IBS/CBS por vigência e a escolha da vigente numa data.</summary>
public class IbsCbsRatesService(IUnitOfWork db)
{
    public IQueryable<IbsCbsRate> QueryAll() => db.Context.IbsCbsRates.AsNoTracking();

    public Task<IbsCbsRate?> GetByIdAsync(int key) =>
        db.Context.IbsCbsRates.FirstOrDefaultAsync(x => x.Key == key);

    public Task<IbsCbsRate?> GetEffectiveAsync(DateOnly date) =>
        db.Context.IbsCbsRates.AsNoTracking()
            .Where(x => x.StartDate <= date)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync();

    public async Task<IbsCbsRate> CreateAsync(IbsCbsRate entity)
    {
        await ValidateAsync(entity, ignoreKey: null);
        entity.Key = 0;
        await db.Context.IbsCbsRates.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<IbsCbsRate?> UpdateAsync(int key, IbsCbsRate entity)
    {
        var existing = await db.Context.IbsCbsRates.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return null;

        await ValidateAsync(entity, ignoreKey: key);

        existing.StartDate = entity.StartDate;
        existing.CbsRate = entity.CbsRate;
        existing.IbsStateRate = entity.IbsStateRate;
        existing.IbsMunicipalRate = entity.IbsMunicipalRate;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int key)
    {
        var existing = await db.Context.IbsCbsRates.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return false;

        db.Context.IbsCbsRates.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    private async Task ValidateAsync(IbsCbsRate entity, int? ignoreKey)
    {
        foreach (var (value, label) in new[]
                 {
                     (entity.CbsRate, "CBS"), (entity.IbsStateRate, "IBS estadual"),
                     (entity.IbsMunicipalRate, "IBS municipal"),
                 })
        {
            if (value is < 0 or > 100)
                throw new DefaultException($"Alíquota de {label} deve estar entre 0 e 100.");
        }

        var duplicated = await db.Context.IbsCbsRates
            .AnyAsync(x => x.StartDate == entity.StartDate && x.Key != ignoreKey);

        if (duplicated)
            throw new DefaultException(
                $"Já existe uma vigência de IBS/CBS iniciando em {entity.StartDate:dd/MM/yyyy}.");
    }
}
