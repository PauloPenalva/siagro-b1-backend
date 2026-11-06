using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>
/// Commodities da posição. Itens e meses têm serviço próprio; a inclusão em profundidade (cabeçalho + itens + meses)
/// aplica as mesmas regras antes de qualquer SaveChanges, e o corpo do cabeçalho nunca os altera depois.
/// </summary>
public class MarketCommodityService(AppDbContext context, IItemService itemService, ILogger<MarketCommodityService> logger,
    MarketExposureCache? exposureCache = null, ICompanyContext? companyContext = null)
    : BaseService<MarketCommodity, string>(context, logger)
{
    public override async Task<MarketCommodity> CreateAsync(MarketCommodity entity)
    {
        MarketCommodityRules.NormalizeHeader(entity);
        var upper = entity.Code.ToUpper();
        if (await context.MarketCommodities.AsNoTracking().AnyAsync(c => c.Code.ToUpper() == upper))
            throw new BusinessException("Já existe uma commodity com este código.");

        var itemCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in entity.Items)
        {
            item.Id = 0;
            item.CommodityCode = entity.Code;
            MarketCommodityRules.NormalizeItemCode(item);
            if (!itemCodes.Add(item.ItemCode))
                throw new BusinessException($"O produto {item.ItemCode} está repetido nesta commodity.");
            await MarketCommodityRules.FillItemNameAsync(itemService, item);
            await MarketCommodityRules.EnsureItemFreeAsync(context, item);
        }

        var deliveryMonths = new HashSet<int>();
        foreach (var month in entity.Months)
        {
            month.Id = 0;
            month.CommodityCode = entity.Code;
            MarketCommodityRules.NormalizeMonth(month);
            if (!deliveryMonths.Add(month.DeliveryMonth))
                throw new BusinessException($"O mês de entrega {month.DeliveryMonth} está repetido nesta commodity.");
        }

        var created = await MarketPersistence.AddAndSaveAsync(context, entity);
        InvalidateExposure();
        return created;
    }

    public override async Task<MarketCommodity?> UpdateAsync(string key, MarketCommodity entity)
    {
        entity.Code = key;
        if (!await context.MarketCommodities.AsNoTracking().AnyAsync(c => c.Code == key))
            throw new KeyNotFoundException("Entity not found.");

        MarketCommodityRules.NormalizeHeader(entity);
        IgnorePayloadChildren(entity);
        var updated = await base.UpdateAsync(key, entity);
        InvalidateExposure();
        return updated;
    }

    public override async Task<bool> DeleteAsync(string key)
    {
        var commodity = await context.MarketCommodities
            .Include(c => c.Items).Include(c => c.Months).FirstOrDefaultAsync(c => c.Code == key);
        if (commodity is null)
            return false;
        if (await context.MarketExposureLimits.AnyAsync(l => l.CommodityCode == key))
            throw new BusinessException("Há limites de exposição desta commodity; exclua os limites ou inative a commodity.");

        context.MarketCommodityItems.RemoveRange(commodity.Items);
        context.MarketCommodityMonths.RemoveRange(commodity.Months);
        context.MarketCommodities.Remove(commodity);
        await context.SaveChangesAsync();
        InvalidateExposure();
        return true;
    }

    // Itens e meses têm serviço próprio. Em PUT a entidade vem solta e as listas são descartadas; em PATCH ela já está
    // rastreada e sai todo filho que o payload trouxe ou alterou (só os rastreados intactos ficam).
    private void IgnorePayloadChildren(MarketCommodity entity)
    {
        if (context.Entry(entity).State == EntityState.Detached)
        {
            entity.Items = [];
            entity.Months = [];
            return;
        }

        foreach (var item in entity.Items.ToList())
        {
            if (context.Entry(item).State == EntityState.Unchanged)
                continue;
            context.Entry(item).State = EntityState.Detached;
            entity.Items.Remove(item);
        }

        foreach (var month in entity.Months.ToList())
        {
            if (context.Entry(month).State == EntityState.Unchanged)
                continue;
            context.Entry(month).State = EntityState.Detached;
            entity.Months.Remove(month);
        }
    }

    /// <summary>Cria, se ainda não existirem (por código), as commodities de exemplo. Devolve quantas foram criadas.</summary>
    public async Task<int> CreateSamplesAsync(CancellationToken cancellationToken)
    {
        var existing = await context.MarketCommodities.Select(c => c.Code).ToListAsync(cancellationToken);
        var missing = BuildSamples().Where(s => !existing.Contains(s.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count == 0)
            return 0;

        context.MarketCommodities.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
        InvalidateExposure();
        return missing.Count;
    }

    // Sem produtos: o cadastro de produtos é de cada cliente.
    private static List<MarketCommodity> BuildSamples() =>
    [
        Sample("SOY", "Soja", [1, 3, 5, 7, 8, 9, 11]),
        Sample("CORN", "Milho", [3, 5, 7, 9, 12]),
    ];

    private static MarketCommodity Sample(string code, string name, int[] futuresMonths) => new()
    {
        Code = code,
        Name = name,
        HedgeExchange = MarketExchange.Cbot,
        Active = true,
        Months = MarketPositionMath.DefaultRules(futuresMonths)
            .Select(r => new MarketCommodityMonth
            {
                CommodityCode = code, DeliveryMonth = r.DeliveryMonth, FuturesMonth = r.FuturesMonth, YearOffset = r.YearOffset,
            }).ToList(),
    };

    /// <summary>A posição lê o cache de 30 s: cadastro de commodity, produto ou mês tem de aparecer já.</summary>
    private void InvalidateExposure()
    {
        if (companyContext is not null) exposureCache?.Invalidate(companyContext.CompanyCode);
    }
}
