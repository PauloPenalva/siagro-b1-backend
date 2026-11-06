using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

/// <summary>Cidade que possui ao menos um frete cadastrado (alimenta o seletor da grade).</summary>
public sealed record MarketFreightCity(string Code, string Name, string StateAbbreviation);

public class MarketFreightRateService(
    AppDbContext context, LiveQuoteStore store, ICompanyContext companyContext, ILogger<MarketFreightRateService> logger)
    : BaseService<MarketFreightRate, int>(context, logger)
{
    public override Task<MarketFreightRate> CreateAsync(MarketFreightRate entity) => CreateAsync(entity, null);

    public override Task<MarketFreightRate?> UpdateAsync(int key, MarketFreightRate entity) => UpdateAsync(key, entity, null);

    /// <summary>Grava o frete carimbando quem criou e quando (UTC).</summary>
    public async Task<MarketFreightRate> CreateAsync(MarketFreightRate entity, string? userName)
    {
        await ValidateAsync(entity);
        entity.CreatedBy = userName;
        entity.CreatedAt = DateTime.UtcNow;
        var created = await MarketPersistence.AddAndSaveAsync(context, entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return created;
    }

    /// <summary>Altera o frete; autor e data de criação são preservados.</summary>
    public async Task<MarketFreightRate?> UpdateAsync(int key, MarketFreightRate entity, string? userName)
    {
        entity.Id = key;
        var original = await context.MarketFreightRates.AsNoTracking()
            .Where(r => r.Id == key)
            .Select(r => new { r.CreatedBy, r.CreatedAt })
            .FirstOrDefaultAsync();
        if (original is null)
            throw new KeyNotFoundException("Entity not found.");

        await ValidateAsync(entity);
        entity.CreatedBy = original.CreatedBy;
        entity.CreatedAt = original.CreatedAt;
        var updated = await base.UpdateAsync(key, entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return updated;
    }

    public override async Task<bool> DeleteAsync(int key)
    {
        var deleted = await base.DeleteAsync(key);
        if (deleted)
            store.NotifyChanged(companyContext.CompanyCode);
        return deleted;
    }

    /// <summary>Frete vigente por destino da cidade: o de maior ValidFrom que já começou. Destino sem frete vigente não aparece.</summary>
    public async Task<IReadOnlyDictionary<int, decimal>> GetEffectiveRatesAsync(
        string municipalityCode, DateOnly today, CancellationToken cancellationToken)
    {
        var limit = today.ToDateTime(TimeOnly.MinValue);
        var rates = await context.MarketFreightRates.AsNoTracking()
            .Where(r => r.MunicipalityCode == municipalityCode && r.ValidFrom <= limit)
            .Select(r => new { r.DestinationId, r.ValidFrom, r.RatePerTon })
            .ToListAsync(cancellationToken);

        return rates
            .GroupBy(r => r.DestinationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ValidFrom).First().RatePerTon);
    }

    public async Task<IReadOnlyList<MarketFreightCity>> GetCitiesAsync(CancellationToken cancellationToken)
    {
        var codes = context.MarketFreightRates.AsNoTracking().Select(r => r.MunicipalityCode).Distinct();
        return await context.Municipalities.AsNoTracking()
            .Where(m => codes.Contains(m.Code))
            .OrderBy(m => m.Name)
            .Select(m => new MarketFreightCity(m.Code, m.Name, m.StateAbbreviation))
            .ToListAsync(cancellationToken);
    }

    private async Task ValidateAsync(MarketFreightRate entity)
    {
        entity.MunicipalityCode = entity.MunicipalityCode?.Trim() ?? string.Empty;
        if (entity.MunicipalityCode.Length == 0)
            throw new BusinessException("Informe a cidade de origem do frete.");
        if (!await context.Municipalities.AnyAsync(m => m.Code == entity.MunicipalityCode))
            throw new BusinessException("Cidade de origem não encontrada.");
        if (!await context.MarketDestinations.AnyAsync(d => d.Id == entity.DestinationId))
            throw new BusinessException("Destino não encontrado.");
        if (entity.RatePerTon < 0)
            throw new BusinessException("O frete por tonelada não pode ser negativo.");

        entity.ValidFrom = entity.ValidFrom.Date;
        if (entity.ValidFrom == default)
            throw new BusinessException("Informe o início da vigência do frete.");

        if (await context.MarketFreightRates.AsNoTracking().AnyAsync(r =>
                r.Id != entity.Id && r.MunicipalityCode == entity.MunicipalityCode
                && r.DestinationId == entity.DestinationId && r.ValidFrom == entity.ValidFrom))
            throw new BusinessException("Já existe um frete para esta cidade, destino e início de vigência.");
    }
}
