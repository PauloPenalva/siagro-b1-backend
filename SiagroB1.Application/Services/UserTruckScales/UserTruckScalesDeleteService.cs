using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.UserTruckScales;

public class UserTruckScalesDeleteService(IUnitOfWork db)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var entity = await db.Context.UserTruckScales.FirstOrDefaultAsync(x => x.Id == key);

        if (entity == null)
            return false;

        db.Context.UserTruckScales.Remove(entity);
        await db.SaveChangesAsync();

        return true;
    }
}
