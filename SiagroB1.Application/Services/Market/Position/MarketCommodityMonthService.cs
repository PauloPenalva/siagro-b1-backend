using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>Mapa de vencimentos de uma commodity: no máximo uma linha por mês de entrega.</summary>
public class MarketCommodityMonthService(AppDbContext context, ILogger<MarketCommodityMonthService> logger,
    MarketExposureCache? exposureCache = null, ICompanyContext? companyContext = null)
    : BaseService<MarketCommodityMonth, int>(context, logger)
{
    public override async Task<MarketCommodityMonth> CreateAsync(MarketCommodityMonth entity)
    {
        entity.Id = 0;
        entity.Commodity = null;
        await ValidateAsync(entity);
        var created = await MarketPersistence.AddAndSaveAsync(context, entity);
        InvalidateExposure();
        return created;
    }

    public override async Task<MarketCommodityMonth?> UpdateAsync(int key, MarketCommodityMonth entity)
    {
        entity.Id = key;
        entity.Commodity = null;
        var currentCommodity = await context.MarketCommodityMonths.AsNoTracking()
            .Where(m => m.Id == key).Select(m => m.CommodityCode).FirstOrDefaultAsync();
        if (currentCommodity is null)
            throw new KeyNotFoundException("Entity not found.");
        if (!string.Equals(currentCommodity, entity.CommodityCode, StringComparison.Ordinal))
            throw new BusinessException("Não é possível mover o mês para outra commodity; exclua e inclua de novo.");

        await ValidateAsync(entity);
        var updated = await base.UpdateAsync(key, entity);
        InvalidateExposure();
        return updated;
    }

    public override async Task<bool> DeleteAsync(int key)
    {
        var deleted = await base.DeleteAsync(key);
        InvalidateExposure();
        return deleted;
    }

    public IQueryable<MarketCommodityMonth> QueryByCommodity(string commodityCode) =>
        QueryAll().Where(m => m.CommodityCode == commodityCode);

    private async Task ValidateAsync(MarketCommodityMonth entity)
    {
        entity.CommodityCode = entity.CommodityCode?.Trim() ?? string.Empty;
        if (!await MarketCommodityRules.CommodityExistsAsync(context, entity.CommodityCode))
            throw new BusinessException("Commodity não encontrada.");

        MarketCommodityRules.NormalizeMonth(entity);
        if (await context.MarketCommodityMonths.AsNoTracking().AnyAsync(m =>
                m.Id != entity.Id && m.CommodityCode == entity.CommodityCode && m.DeliveryMonth == entity.DeliveryMonth))
            throw new BusinessException($"Já existe uma regra para o mês de entrega {entity.DeliveryMonth} nesta commodity.");
    }

    /// <summary>A posição lê o cache de 30 s: cadastro de commodity, produto ou mês tem de aparecer já.</summary>
    private void InvalidateExposure()
    {
        if (companyContext is not null) exposureCache?.Invalidate(companyContext.CompanyCode);
    }
}
