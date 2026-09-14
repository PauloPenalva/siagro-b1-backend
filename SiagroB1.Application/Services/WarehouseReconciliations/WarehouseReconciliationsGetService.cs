using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsGetService(IUnitOfWork db)
{
    public IQueryable<WarehouseReconciliation> QueryAll() =>
        db.Context.WarehouseReconciliations.AsNoTracking();

    // AsNoTracking é o que faz o PATCH funcionar (mesmo motivo de OwnershipTransfersGetService).
    public Task<WarehouseReconciliation?> GetByIdAsync(Guid key) =>
        db.Context.WarehouseReconciliations.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
