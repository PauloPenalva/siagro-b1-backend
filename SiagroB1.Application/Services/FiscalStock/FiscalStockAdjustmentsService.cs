using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.FiscalStock;

/// <summary>
/// Ajuste manual do estoque fiscal (entrada ou saída avulsa, com motivo). Só existe no modo STANDALONE
/// (o controller recusa SAPB1). Respeita a trava do mês fechado e não deixa a saída passar do estoque.
/// </summary>
public class FiscalStockAdjustmentsService(
    IUnitOfWork db, FiscalStockPeriodGuard? periodGuard = null, FiscalStockLedgerLoader? ledgerLoader = null)
    : IFiscalStockAdjustmentsService
{
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);
    private readonly FiscalStockLedgerLoader _ledgerLoader = ledgerLoader ?? new FiscalStockLedgerLoader(db);

    private const int UnitOfMeasureMaxLength = 4;
    private static readonly CultureInfo PtBr = new("pt-BR");

    public IQueryable<FiscalStockAdjustment> QueryAll() => db.Context.FiscalStockAdjustments.AsNoTracking();

    public Task<FiscalStockAdjustment?> GetByKeyAsync(Guid key) =>
        db.Context.FiscalStockAdjustments.FirstOrDefaultAsync(x => x.Key == key);

    public async Task<FiscalStockAdjustment> CreateAsync(FiscalStockAdjustment entity, string userName)
    {
        Validate(entity);
        await EnsureItemExistsAsync(entity);
        await _periodGuard.EnsureOpenAsync(entity.BranchCode, entity.AdjustmentDate);
        await FillFromItemAsync(entity);
        await EnsureOutFitsAsync(entity, null);

        entity.Key = Guid.Empty;
        entity.DocNumber = await NextDocNumberAsync(entity.BranchCode);
        entity.CreatedBy = userName;
        await db.Context.FiscalStockAdjustments.AddAsync(entity);
        await SaveAsync();
        return entity;
    }

    public async Task<FiscalStockAdjustment?> UpdateAsync(Guid key, FiscalStockAdjustment entity)
    {
        var existing = await db.Context.FiscalStockAdjustments.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return null;

        Validate(entity);
        await EnsureItemExistsAsync(entity);
        // O controller aplica o PATCH na própria entidade rastreada: o valor gravado está nos OriginalValues.
        var entry = db.Context.Entry(existing);
        var storedBranch = entry.OriginalValues.GetValue<string>(nameof(FiscalStockAdjustment.BranchCode));
        var storedDate = entry.OriginalValues.GetValue<DateTime>(nameof(FiscalStockAdjustment.AdjustmentDate));
        await _periodGuard.EnsureOpenAsync(storedBranch, storedDate);
        await _periodGuard.EnsureOpenAsync(entity.BranchCode, entity.AdjustmentDate);

        // O número (AJ) nasce na criação; mudar de filial renumera na nova filial.
        var branchChanged = storedBranch != entity.BranchCode;

        existing.BranchCode = entity.BranchCode;
        existing.AdjustmentDate = entity.AdjustmentDate;
        existing.ItemCode = entity.ItemCode;
        existing.Direction = entity.Direction;
        existing.Quantity = entity.Quantity;
        existing.TotalValue = entity.TotalValue;
        existing.Reason = entity.Reason;
        existing.UnitOfMeasureCode = entity.UnitOfMeasureCode;
        existing.UpdatedAt = DateTime.Now;
        await FillFromItemAsync(existing);
        await EnsureOutFitsAsync(existing, key);
        if (branchChanged) existing.DocNumber = await NextDocNumberAsync(existing.BranchCode);

        await SaveAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid key)
    {
        var existing = await db.Context.FiscalStockAdjustments.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return false;

        await _periodGuard.EnsureOpenAsync(existing.BranchCode, existing.AdjustmentDate);
        db.Context.FiscalStockAdjustments.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Índice único filial+número: dois ajustes ao mesmo tempo pegam o mesmo AJ; o segundo tenta de novo.</summary>
    private async Task SaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (FiscalStockPeriodsService.IsUniqueViolation(e))
        {
            throw new DefaultException("Outro ajuste foi gravado ao mesmo tempo. Tente de novo.");
        }
    }

    private static void Validate(FiscalStockAdjustment entity)
    {
        if (string.IsNullOrWhiteSpace(entity.BranchCode))
            throw new DefaultException("Informe a filial.");

        if (string.IsNullOrWhiteSpace(entity.ItemCode))
            throw new DefaultException("Informe o produto.");

        if (entity.AdjustmentDate == default)
            throw new DefaultException("Informe a data do ajuste.");

        if (entity.Quantity <= 0)
            throw new DefaultException("Informe uma quantidade maior que zero.");

        if (entity.TotalValue < 0)
            throw new DefaultException("O valor não pode ser negativo.");

        if (string.IsNullOrWhiteSpace(entity.Reason))
            throw new DefaultException("Informe o motivo do ajuste.");

        entity.Reason = entity.Reason.Trim();
    }

    private async Task EnsureItemExistsAsync(FiscalStockAdjustment entity)
    {
        if (!await db.Context.Items.AnyAsync(x => x.ItemCode == entity.ItemCode))
            throw new DefaultException("Produto não encontrado.");
    }

    /// <summary>Saída: a quantidade e o valor não podem passar do que o PEPS tem em estoque na data do ajuste.</summary>
    private async Task EnsureOutFitsAsync(FiscalStockAdjustment entity, Guid? ignoreKey)
    {
        if (entity.Direction != FiscalStockAdjustmentDirection.Out) return;

        var ledger = (await _ledgerLoader.LoadAsync(
                entity.BranchCode, entity.ItemCode, entity.AdjustmentDate.Date, excludeAdjustmentKey: ignoreKey))
            .FirstOrDefault(l => l.ItemCode == entity.ItemCode);

        var availableQuantity = ledger == null ? 0m : ledger.Result.OpenLayers.Sum(l => l.Quantity) - ledger.Result.Deficit;
        // Saldo em valor = camadas + resíduo (diferença de saídas anteriores pelo valor informado).
        var availableValue = ledger == null ? 0m : ledger.Result.OpenLayers.Sum(l => l.Value) + ledger.Result.ResidualValue;

        if (entity.Quantity > availableQuantity)
            throw new DefaultException(
                $"Saldo insuficiente para o ajuste de saída: disponível {Math.Max(availableQuantity, 0m).ToString("#,##0.######", PtBr)}.");

        if (entity.TotalValue > availableValue)
            throw new DefaultException(
                $"O valor do ajuste passa do valor em estoque: disponível {Math.Max(availableValue, 0m).ToString("C2", PtBr)}.");
    }

    private async Task<string> NextDocNumberAsync(string branchCode)
    {
        var numbers = await db.Context.FiscalStockAdjustments
            .Where(x => x.BranchCode == branchCode && x.DocNumber.StartsWith("AJ"))
            .Select(x => x.DocNumber).ToListAsync();

        var last = numbers
            .Select(n => int.TryParse(n.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();

        return string.Create(CultureInfo.InvariantCulture, $"AJ{last + 1:D6}");
    }

    private async Task FillFromItemAsync(FiscalStockAdjustment entity)
    {
        var name = await db.Context.Items.Where(x => x.ItemCode == entity.ItemCode)
            .Select(x => x.ItemName).FirstOrDefaultAsync();
        var uom = await db.Context.Set<ItemComplement>().Where(x => x.ItemCode == entity.ItemCode)
            .Select(x => x.CommercialUnitOfMeasureCode).FirstOrDefaultAsync();

        entity.ItemName = name;

        var unit = entity.UnitOfMeasureCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(unit)) unit = uom?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(unit))
            throw new DefaultException("Informe a unidade de medida.");

        if (unit.Length > UnitOfMeasureMaxLength)
            throw new DefaultException("Unidade de medida inválida.");

        entity.UnitOfMeasureCode = unit;
    }
}
