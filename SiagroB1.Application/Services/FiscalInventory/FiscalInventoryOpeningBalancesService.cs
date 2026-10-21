using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.FiscalInventory;

/// <summary>
/// Cadastro do saldo inicial valorizado do estoque fiscal (Kardex Fiscal), um lançamento por filial + produto.
/// Só existe no modo STANDALONE (o controller recusa SAPB1). Nome e unidade vêm do cadastro local do produto.
/// </summary>
public class FiscalInventoryOpeningBalancesService(IUnitOfWork db, FiscalStockPeriodGuard? periodGuard = null)
    : IFiscalInventoryOpeningBalancesService
{
    /// <summary>Trava do mês fechado: o saldo inicial não entra, muda nem sai de mês fechado (data antiga e nova).</summary>
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);

    /// <summary>Largura da coluna UnitOfMeasureCode (VARCHAR(4)).</summary>
    private const int UnitOfMeasureMaxLength = 4;

    public IQueryable<FiscalInventoryOpeningBalance> QueryAll() =>
        db.Context.FiscalInventoryOpeningBalances.AsNoTracking();

    public Task<FiscalInventoryOpeningBalance?> GetByKeyAsync(Guid key) =>
        db.Context.FiscalInventoryOpeningBalances.FirstOrDefaultAsync(x => x.Key == key);

    public async Task<FiscalInventoryOpeningBalance> CreateAsync(FiscalInventoryOpeningBalance entity)
    {
        await ValidateAsync(entity, ignoreKey: null);
        await _periodGuard.EnsureOpenAsync(entity.BranchCode, entity.BalanceDate);
        await FillFromItemAsync(entity);
        entity.Key = Guid.Empty;
        await db.Context.FiscalInventoryOpeningBalances.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<FiscalInventoryOpeningBalance?> UpdateAsync(Guid key, FiscalInventoryOpeningBalance entity)
    {
        var existing = await db.Context.FiscalInventoryOpeningBalances.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return null;

        await ValidateAsync(entity, ignoreKey: key);
        await _periodGuard.EnsureOpenAsync(existing.BranchCode, existing.BalanceDate);
        await _periodGuard.EnsureOpenAsync(entity.BranchCode, entity.BalanceDate);

        existing.BranchCode = entity.BranchCode;
        existing.ItemCode = entity.ItemCode;
        existing.BalanceDate = entity.BalanceDate;
        existing.Quantity = entity.Quantity;
        existing.TotalValue = entity.TotalValue;
        existing.UnitOfMeasureCode = entity.UnitOfMeasureCode;
        existing.UpdatedAt = DateTime.Now;
        await FillFromItemAsync(existing);

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid key)
    {
        var existing = await db.Context.FiscalInventoryOpeningBalances.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return false;

        await _periodGuard.EnsureOpenAsync(existing.BranchCode, existing.BalanceDate);
        db.Context.FiscalInventoryOpeningBalances.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    private async Task ValidateAsync(FiscalInventoryOpeningBalance entity, Guid? ignoreKey)
    {
        if (string.IsNullOrWhiteSpace(entity.BranchCode))
            throw new DefaultException("Informe a filial.");

        if (string.IsNullOrWhiteSpace(entity.ItemCode))
            throw new DefaultException("Informe o produto.");

        if (entity.BalanceDate == default)
            throw new DefaultException("Informe a data do saldo.");

        if (entity.Quantity <= 0)
            throw new DefaultException("Informe uma quantidade maior que zero.");

        if (entity.TotalValue < 0)
            throw new DefaultException("O valor não pode ser negativo.");

        if (!await db.Context.Items.AnyAsync(x => x.ItemCode == entity.ItemCode))
            throw new DefaultException("Produto não encontrado.");

        var duplicated = await db.Context.FiscalInventoryOpeningBalances
            .AnyAsync(x => x.BranchCode == entity.BranchCode && x.ItemCode == entity.ItemCode && x.Key != ignoreKey);

        if (duplicated)
            throw new DefaultException("Já existe saldo inicial para este produto nesta filial.");
    }

    private async Task FillFromItemAsync(FiscalInventoryOpeningBalance entity)
    {
        var name = await db.Context.Items.Where(x => x.ItemCode == entity.ItemCode)
            .Select(x => x.ItemName).FirstOrDefaultAsync();
        var uom = await db.Context.Set<ItemComplement>().Where(x => x.ItemCode == entity.ItemCode)
            .Select(x => x.CommercialUnitOfMeasureCode).FirstOrDefaultAsync();

        entity.ItemName = name;

        // A unidade informada pelo usuário prevalece; sem ela, vale a comercial do produto.
        var unit = entity.UnitOfMeasureCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(unit)) unit = uom?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(unit))
            throw new DefaultException("Informe a unidade de medida.");

        if (unit.Length > UnitOfMeasureMaxLength)
            throw new DefaultException("Unidade de medida inválida.");

        entity.UnitOfMeasureCode = unit;
    }
}
