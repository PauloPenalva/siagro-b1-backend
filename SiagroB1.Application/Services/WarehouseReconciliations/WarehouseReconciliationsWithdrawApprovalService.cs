using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsWithdrawApprovalService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.InApproval)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL"].Value);

        r.Status = WarehouseReconciliationStatus.Draft;
        r.SentAt = null;
        r.SentBy = null;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
