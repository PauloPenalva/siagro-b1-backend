using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

/// <summary>Cabeçalho da tabela de preços. O produto é validado por <see cref="IItemService"/> (vale em STANDALONE e SAPB1).</summary>
public class MarketPriceTableService(
    AppDbContext context, IItemService itemService, LiveQuoteStore store, ICompanyContext companyContext,
    ILogger<MarketPriceTableService> logger)
    : BaseService<MarketPriceTable, int>(context, logger)
{
    public override Task<MarketPriceTable> CreateAsync(MarketPriceTable entity) => CreateAsync(entity, null);

    public override Task<MarketPriceTable?> UpdateAsync(int key, MarketPriceTable entity) => UpdateAsync(key, entity, null);

    public async Task<MarketPriceTable> CreateAsync(MarketPriceTable entity, string? userName)
    {
        await ValidateAsync(entity, userName);
        var created = await base.CreateAsync(entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return created;
    }

    public async Task<MarketPriceTable?> UpdateAsync(int key, MarketPriceTable entity, string? userName)
    {
        entity.Id = key;
        await ValidateAsync(entity, userName);
        // As linhas têm serviço próprio: o corpo do cabeçalho não as altera.
        entity.Lines = [];
        var updated = await base.UpdateAsync(key, entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return updated;
    }

    public override async Task<bool> DeleteAsync(int key)
    {
        var table = await context.MarketPriceTables.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == key);
        if (table is null)
            return false;

        context.MarketPriceTableLines.RemoveRange(table.Lines);
        context.MarketPriceTables.Remove(table);
        await context.SaveChangesAsync();
        store.NotifyChanged(companyContext.CompanyCode);
        return true;
    }

    private async Task ValidateAsync(MarketPriceTable entity, string? userName)
    {
        entity.Description = entity.Description?.Trim() ?? string.Empty;
        if (entity.Description.Length == 0)
            throw new BusinessException("Informe a descrição da tabela de preços.");
        if (entity.Description.Length > 100)
            throw new BusinessException("A descrição deve ter no máximo 100 caracteres.");

        entity.ItemCode = entity.ItemCode?.Trim() ?? string.Empty;
        if (entity.ItemCode.Length == 0)
            throw new BusinessException("Informe o produto.");
        var item = await itemService.GetByIdAsync(entity.ItemCode)
                   ?? throw new BusinessException("Produto não encontrado.");
        entity.ItemName = item.ItemName;

        entity.HarvestSeasonCode = entity.HarvestSeasonCode?.Trim() ?? string.Empty;
        if (entity.HarvestSeasonCode.Length == 0)
            throw new BusinessException("Informe a safra.");
        if (!await context.HarvestSeasons.AnyAsync(h => h.Code == entity.HarvestSeasonCode))
            throw new BusinessException("Safra não encontrada.");

        entity.UpdatedBy = userName;
        entity.UpdatedAt = DateTime.UtcNow;
    }
}
