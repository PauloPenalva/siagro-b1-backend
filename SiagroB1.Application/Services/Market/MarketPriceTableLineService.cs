using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

/// <summary>Linhas da tabela de preços. Toda gravação carimba o cabeçalho (UpdatedBy/UpdatedAt) na mesma transação.</summary>
public class MarketPriceTableLineService(
    AppDbContext context, LiveQuoteStore store, ICompanyContext companyContext, ILogger<MarketPriceTableLineService> logger)
    : BaseService<MarketPriceTableLine, int>(context, logger)
{
    public override Task<MarketPriceTableLine> CreateAsync(MarketPriceTableLine entity) => CreateAsync(entity, null);

    public override Task<MarketPriceTableLine?> UpdateAsync(int key, MarketPriceTableLine entity) =>
        UpdateAsync(key, entity, null);

    public override Task<bool> DeleteAsync(int key) => DeleteAsync(key, null);

    public async Task<MarketPriceTableLine> CreateAsync(MarketPriceTableLine entity, string? userName)
    {
        await ValidateAsync(entity);
        await StampHeaderAsync(entity.PriceTableId, userName);
        var created = await MarketPersistence.AddAndSaveAsync(context, entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return created;
    }

    public async Task<MarketPriceTableLine?> UpdateAsync(int key, MarketPriceTableLine entity, string? userName)
    {
        entity.Id = key;
        var currentTableId = await context.MarketPriceTableLines.AsNoTracking()
            .Where(l => l.Id == key).Select(l => (int?)l.PriceTableId).FirstOrDefaultAsync();
        if (currentTableId is null)
            throw new KeyNotFoundException("Entity not found.");
        if (currentTableId != entity.PriceTableId)
            throw new BusinessException("Não é possível mover a linha para outra tabela de preços.");

        await ValidateAsync(entity);
        await StampHeaderAsync(entity.PriceTableId, userName);
        var updated = await base.UpdateAsync(key, entity);
        store.NotifyChanged(companyContext.CompanyCode);
        return updated;
    }

    public async Task<bool> DeleteAsync(int key, string? userName)
    {
        var tableId = await context.MarketPriceTableLines.AsNoTracking()
            .Where(l => l.Id == key).Select(l => (int?)l.PriceTableId).FirstOrDefaultAsync();
        if (tableId is null)
            return false;

        await StampHeaderAsync(tableId.Value, userName);
        var deleted = await base.DeleteAsync(key);
        if (deleted)
            store.NotifyChanged(companyContext.CompanyCode);
        return deleted;
    }

    public IQueryable<MarketPriceTableLine> QueryByTable(int priceTableId) =>
        QueryAll().Where(l => l.PriceTableId == priceTableId);

    // Fica pendente no contexto e sai no SaveChanges da própria linha.
    private async Task StampHeaderAsync(int priceTableId, string? userName)
    {
        var header = await context.MarketPriceTables.FindAsync(priceTableId)
                     ?? throw new BusinessException("Tabela de preços não encontrada.");
        header.UpdatedBy = userName;
        header.UpdatedAt = DateTime.UtcNow;
    }

    private async Task ValidateAsync(MarketPriceTableLine entity)
    {
        if (!await context.MarketPriceTables.AsNoTracking().AnyAsync(t => t.Id == entity.PriceTableId))
            throw new BusinessException("Tabela de preços não encontrada.");

        await MarketPriceTableLineRules.ValidateAsync(context, entity);
    }
}
