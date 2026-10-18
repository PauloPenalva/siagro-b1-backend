using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Cadastro das alíquotas da contribuição social do produtor rural por regime e vigência, e a
/// escolha da vigente numa data.
/// </summary>
public class RuralContributionRatesService(IUnitOfWork db)
{
    public IQueryable<RuralContributionRate> QueryAll() => db.Context.RuralContributionRates.AsNoTracking();

    public Task<RuralContributionRate?> GetByIdAsync(int key) =>
        db.Context.RuralContributionRates.FirstOrDefaultAsync(x => x.Key == key);

    public Task<RuralContributionRate?> GetEffectiveAsync(RuralContributionScheme scheme, DateOnly date) =>
        db.Context.RuralContributionRates.AsNoTracking()
            .Where(x => x.Scheme == scheme && x.StartDate <= date)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync();

    public async Task<RuralContributionRate> CreateAsync(RuralContributionRate entity)
    {
        await ValidateAsync(entity, ignoreKey: null);
        entity.Key = 0;
        await db.Context.RuralContributionRates.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<RuralContributionRate?> UpdateAsync(int key, RuralContributionRate entity)
    {
        var existing = await db.Context.RuralContributionRates.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return null;

        await ValidateAsync(entity, ignoreKey: key);

        existing.StartDate = entity.StartDate;
        existing.Scheme = entity.Scheme;
        existing.InssRate = entity.InssRate;
        existing.RatRate = entity.RatRate;
        existing.SenarRate = entity.SenarRate;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int key)
    {
        var existing = await db.Context.RuralContributionRates.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return false;

        db.Context.RuralContributionRates.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    private async Task ValidateAsync(RuralContributionRate entity, int? ignoreKey)
    {
        foreach (var (value, label) in new[]
                 {
                     (entity.InssRate, "INSS"), (entity.RatRate, "RAT"), (entity.SenarRate, "SENAR"),
                 })
        {
            if (value is < 0 or > 100)
                throw new DefaultException($"Alíquota de {label} deve estar entre 0 e 100.");
        }

        var duplicated = await db.Context.RuralContributionRates
            .AnyAsync(x => x.Scheme == entity.Scheme && x.StartDate == entity.StartDate && x.Key != ignoreKey);

        if (duplicated)
        {
            var scheme = entity.Scheme == RuralContributionScheme.PayrollOption ? "Optante pela folha" : "Comercialização";
            throw new DefaultException(
                $"Já existe uma vigência de contribuição social ({scheme}) iniciando em {entity.StartDate:dd/MM/yyyy}.");
        }
    }
}
