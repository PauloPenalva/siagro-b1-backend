using Microsoft.EntityFrameworkCore;

using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class ItemComplementService(IUnitOfWork db) : IItemComplementService
{
    public async Task<ItemComplementDto?> GetAsync(string itemCode)
    {
        return await db.Context.ItemComplements
            .Where(x => x.ItemCode == itemCode)
            .Select(x => new ItemComplementDto
            {
                ItemCode = x.ItemCode,
                CommercialUnitOfMeasureCode = x.CommercialUnitOfMeasureCode,
                CommercialFactor = x.CommercialFactor,
                TaxableUnitOfMeasureCode = x.TaxableUnitOfMeasureCode,
                TaxableFactor = x.TaxableFactor,
                ControlsRoyalties = x.ControlsRoyalties,
            })
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    public async Task<ItemComplementDto> SetAsync(
        string itemCode, string? commercialUnitOfMeasureCode, decimal? commercialFactor,
        bool? controlsRoyalties = null, ItemTaxableUnitInput? taxableUnit = null)
    {
        var taxable = taxableUnit is null ? null : NormalizeTaxableUnit(taxableUnit);

        var entity = await db.Context.ItemComplements
            .FirstOrDefaultAsync(x => x.ItemCode == itemCode);

        if (entity == null)
        {
            entity = new ItemComplement { ItemCode = itemCode };
            await db.Context.ItemComplements.AddAsync(entity);
        }

        // String vazia vinda do diálogo equivale a "não informado": guardar "" faria a UoM parecer
        // configurada e o faturamento tentaria converter o preço por um fator inexistente.
        entity.CommercialUnitOfMeasureCode =
            string.IsNullOrWhiteSpace(commercialUnitOfMeasureCode) ? null : commercialUnitOfMeasureCode;
        entity.CommercialFactor = commercialFactor;

        if (controlsRoyalties.HasValue)
            entity.ControlsRoyalties = controlsRoyalties.Value;

        if (taxableUnit is not null)
        {
            entity.TaxableUnitOfMeasureCode = taxable?.UnitOfMeasureCode;
            entity.TaxableFactor = taxable?.Factor;
        }

        await db.SaveChangesAsync();

        return new ItemComplementDto
        {
            ItemCode = entity.ItemCode,
            CommercialUnitOfMeasureCode = entity.CommercialUnitOfMeasureCode,
            CommercialFactor = entity.CommercialFactor,
            TaxableUnitOfMeasureCode = entity.TaxableUnitOfMeasureCode,
            TaxableFactor = entity.TaxableFactor,
            ControlsRoyalties = entity.ControlsRoyalties,
        };
    }

    /// <summary>
    /// Unidade tributável de exportação (spec 2026-10-14 §2): os dois juntos, fator positivo, unidade de até
    /// 6 caracteres (tamanho do uTrib) em maiúsculas. Os dois vazios = limpar (devolve nulo).
    /// </summary>
    private static ItemTaxableUnitInput? NormalizeTaxableUnit(ItemTaxableUnitInput input)
    {
        var unit = string.IsNullOrWhiteSpace(input.UnitOfMeasureCode) ? null : input.UnitOfMeasureCode.Trim().ToUpperInvariant();

        if (unit is null && input.Factor is null)
            return null;
        if (unit is null || input.Factor is null)
            throw new DefaultException("Informe a unidade tributável e o fator juntos.");
        if (input.Factor <= 0)
            throw new DefaultException("O fator da unidade tributável deve ser maior que zero.");
        if (unit.Length > 6)
            throw new DefaultException("A unidade tributável deve ter no máximo 6 caracteres.");

        return new ItemTaxableUnitInput(unit, input.Factor);
    }
}
