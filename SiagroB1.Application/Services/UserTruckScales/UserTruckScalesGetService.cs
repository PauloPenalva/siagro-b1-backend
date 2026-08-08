using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.UserTruckScales;

public class UserTruckScalesGetService(IUnitOfWork db)
{
    /// <summary>Include da balança: a grade mostra o nome, não só o código.</summary>
    public IQueryable<UserTruckScale> QueryAll() =>
        db.Context.UserTruckScales.Include(x => x.TruckScale);

    public async Task<UserTruckScale?> GetByIdAsync(Guid key) =>
        await db.Context.UserTruckScales
            .Include(x => x.TruckScale)
            .FirstOrDefaultAsync(x => x.Id == key);
}
