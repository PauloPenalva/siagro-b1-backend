using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.UserTruckScales;

public class UserTruckScalesCreateService(IUnitOfWork db)
{
    public async Task<UserTruckScale> ExecuteAsync(UserTruckScale entity)
    {
        // Mensagem de negócio antes de o índice único estourar como erro de banco.
        var duplicated = await db.Context.UserTruckScales
            .AnyAsync(x => x.Username == entity.Username && x.Purpose == entity.Purpose);

        if (duplicated)
            throw new DefaultException(
                "Este usuário já possui uma balança configurada para esta finalidade.");

        await db.Context.UserTruckScales.AddAsync(entity);
        await db.SaveChangesAsync();

        return entity;
    }
}
