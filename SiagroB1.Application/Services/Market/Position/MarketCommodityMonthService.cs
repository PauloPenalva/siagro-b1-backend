using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>Mapa de vencimentos de uma commodity: no máximo uma linha por mês de entrega.</summary>
public class MarketCommodityMonthService(AppDbContext context, ILogger<MarketCommodityMonthService> logger)
    : BaseService<MarketCommodityMonth, int>(context, logger)
{
    public override async Task<MarketCommodityMonth> CreateAsync(MarketCommodityMonth entity)
    {
        await ValidateAsync(entity);
        return await MarketPersistence.AddAndSaveAsync(context, entity);
    }

    public override async Task<MarketCommodityMonth?> UpdateAsync(int key, MarketCommodityMonth entity)
    {
        entity.Id = key;
        var currentCommodity = await context.MarketCommodityMonths.AsNoTracking()
            .Where(m => m.Id == key).Select(m => m.CommodityCode).FirstOrDefaultAsync();
        if (currentCommodity is null)
            throw new KeyNotFoundException("Entity not found.");
        if (!string.Equals(currentCommodity, entity.CommodityCode, StringComparison.Ordinal))
            throw new BusinessException("Não é possível mover o mês para outra commodity; exclua e inclua de novo.");

        await ValidateAsync(entity);
        return await base.UpdateAsync(key, entity);
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
}
