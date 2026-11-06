using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public class MarketDestinationService(
    AppDbContext context, LiveQuoteStore store, ICompanyContext companyContext, ILogger<MarketDestinationService> logger)
    : BaseService<MarketDestination, int>(context, logger)
{
    public override async Task<MarketDestination> CreateAsync(MarketDestination entity)
    {
        await ValidateAsync(entity);
        var created = await base.CreateAsync(entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return created;
    }

    public override async Task<MarketDestination?> UpdateAsync(int key, MarketDestination entity)
    {
        entity.Id = key;
        await ValidateAsync(entity);
        var updated = await base.UpdateAsync(key, entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return updated;
    }

    public override async Task<bool> DeleteAsync(int key)
    {
        if (await context.MarketFreightRates.AnyAsync(r => r.DestinationId == key))
            throw new BusinessException("O destino possui fretes cadastrados; inative-o em vez de excluir.");
        if (await context.MarketPriceTableLines.AnyAsync(l => l.DestinationId == key))
            throw new BusinessException("O destino é usado em linhas de tabelas de preços; inative-o em vez de excluir.");

        var deleted = await base.DeleteAsync(key);
        if (deleted)
            store.NotifyChanged(companyContext.CompanyCode);
        return deleted;
    }

    private async Task ValidateAsync(MarketDestination entity)
    {
        entity.Name = entity.Name?.Trim() ?? string.Empty;
        if (entity.Name.Length == 0)
            throw new BusinessException("Informe o nome do destino.");
        if (entity.Name.Length > 100)
            throw new BusinessException("O nome do destino deve ter no máximo 100 caracteres.");

        var upper = entity.Name.ToUpper();
        if (await context.MarketDestinations.AsNoTracking().AnyAsync(d => d.Id != entity.Id && d.Name.ToUpper() == upper))
            throw new BusinessException("Já existe um destino com este nome.");
    }
}
