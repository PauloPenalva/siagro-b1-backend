using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.UserTruckScales;

public class UserTruckScalesUpdateService(IUnitOfWork db)
{
    public async Task<UserTruckScale> ExecuteAsync(Guid key, UserTruckScale entity)
    {
        if (!await db.Context.UserTruckScales.AnyAsync(x => x.Id == key))
            throw new NotFoundException("Balança do usuário não encontrada.");

        // A entidade já vem rastreada do GetByIdAsync com o Delta aplicado; aqui só se checa a
        // duplicidade contra as OUTRAS linhas antes de gravar.
        var duplicated = await db.Context.UserTruckScales
            .AnyAsync(x => x.Id != key && x.Username == entity.Username && x.Purpose == entity.Purpose);

        if (duplicated)
            throw new DefaultException(
                "Este usuário já possui uma balança configurada para esta finalidade.");

        await db.SaveChangesAsync();

        return entity;
    }
}
