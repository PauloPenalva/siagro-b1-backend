using Microsoft.EntityFrameworkCore;

using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
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
                ControlsRoyalties = x.ControlsRoyalties,
            })
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    public async Task<ItemComplementDto> SetAsync(
        string itemCode, string? commercialUnitOfMeasureCode, decimal? commercialFactor,
        bool? controlsRoyalties = null)
    {
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

        await db.SaveChangesAsync();

        return new ItemComplementDto
        {
            ItemCode = entity.ItemCode,
            CommercialUnitOfMeasureCode = entity.CommercialUnitOfMeasureCode,
            CommercialFactor = entity.CommercialFactor,
            ControlsRoyalties = entity.ControlsRoyalties,
        };
    }
}
