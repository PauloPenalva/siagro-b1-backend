using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsDeleteService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key)
    {
        var reason = await db.Context.WarehouseReconciliationReasons.FirstOrDefaultAsync(x => x.Key == key)
                     ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_REASON_NOT_FOUND"].Value);

        // Motivo em uso é histórico de conferência: desativa-se, não se apaga.
        if (await db.Context.WarehouseReconciliations.AnyAsync(x => x.ReasonKey == key))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_IN_USE"].Value);

        db.Context.WarehouseReconciliationReasons.Remove(reason);
        await db.SaveChangesAsync();
    }
}
