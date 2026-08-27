using Microsoft.EntityFrameworkCore;

using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class WarehouseComplementService(IUnitOfWork db) : IWarehouseComplementService
{
    public async Task<WarehouseComplementDto?> GetAsync(string warehouseCode)
    {
        return await db.Context.WarehouseComplements
            .Where(x => x.WarehouseCode == warehouseCode)
            .Select(x => new WarehouseComplementDto
            {
                WarehouseCode = x.WarehouseCode,
                IsParticipant = x.IsParticipant,
                IsOwn = x.IsOwn,
                Notes = x.Notes,
            })
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    public async Task<WarehouseComplementDto> SetAsync(
        string warehouseCode, bool isParticipant, bool isOwn, string? notes)
    {
        var entity = await db.Context.WarehouseComplements
            .FirstOrDefaultAsync(x => x.WarehouseCode == warehouseCode);

        if (entity == null)
        {
            entity = new WarehouseComplement { WarehouseCode = warehouseCode };
            await db.Context.WarehouseComplements.AddAsync(entity);
        }

        entity.IsParticipant = isParticipant;
        entity.IsOwn = isOwn;
        // Campo esvaziado na tela chega como "" — guardar nulo mantém "sem observação" com um
        // único valor, em vez de dois que a query teria de tratar como iguais.
        entity.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        await db.SaveChangesAsync();

        return new WarehouseComplementDto
        {
            WarehouseCode = entity.WarehouseCode,
            IsParticipant = entity.IsParticipant,
            IsOwn = entity.IsOwn,
            Notes = entity.Notes,
        };
    }
}
