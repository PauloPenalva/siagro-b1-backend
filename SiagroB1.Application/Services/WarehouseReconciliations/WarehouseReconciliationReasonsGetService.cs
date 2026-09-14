using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsGetService(IUnitOfWork db)
{
    public IQueryable<WarehouseReconciliationReason> QueryAll() =>
        db.Context.WarehouseReconciliationReasons.AsNoTracking();

    // AsNoTracking: o PATCH aplica o Delta sobre esta instância e o Update carrega a sua cópia.
    public Task<WarehouseReconciliationReason?> GetByIdAsync(Guid key) =>
        db.Context.WarehouseReconciliationReasons.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
