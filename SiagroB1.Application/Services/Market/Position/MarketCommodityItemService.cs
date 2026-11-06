using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>Produtos de uma commodity. O nome vem sempre do cadastro de itens; um produto só pertence a uma commodity.</summary>
public class MarketCommodityItemService(AppDbContext context, IItemService itemService, ILogger<MarketCommodityItemService> logger)
    : BaseService<MarketCommodityItem, int>(context, logger)
{
    public override async Task<MarketCommodityItem> CreateAsync(MarketCommodityItem entity)
    {
        entity.Id = 0;
        entity.Commodity = null;
        await ValidateAsync(entity);
        return await MarketPersistence.AddAndSaveAsync(context, entity);
    }

    public override async Task<MarketCommodityItem?> UpdateAsync(int key, MarketCommodityItem entity)
    {
        entity.Id = key;
        entity.Commodity = null;
        var currentCommodity = await context.MarketCommodityItems.AsNoTracking()
            .Where(i => i.Id == key).Select(i => i.CommodityCode).FirstOrDefaultAsync();
        if (currentCommodity is null)
            throw new KeyNotFoundException("Entity not found.");
        if (!string.Equals(currentCommodity, entity.CommodityCode, StringComparison.Ordinal))
            throw new BusinessException("Não é possível mover o produto para outra commodity; exclua e inclua de novo.");

        await ValidateAsync(entity);
        return await base.UpdateAsync(key, entity);
    }

    public IQueryable<MarketCommodityItem> QueryByCommodity(string commodityCode) =>
        QueryAll().Where(i => i.CommodityCode == commodityCode);

    private async Task ValidateAsync(MarketCommodityItem entity)
    {
        entity.CommodityCode = entity.CommodityCode?.Trim() ?? string.Empty;
        if (!await MarketCommodityRules.CommodityExistsAsync(context, entity.CommodityCode))
            throw new BusinessException("Commodity não encontrada.");

        MarketCommodityRules.NormalizeItemCode(entity);
        await MarketCommodityRules.FillItemNameAsync(itemService, entity);
        await MarketCommodityRules.EnsureItemFreeAsync(context, entity);
    }
}
