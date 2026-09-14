using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsRejectService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.InApproval)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL"].Value);

        // Mesmo carimbo dos contratos rejeitados (PurchaseContractsRejectService): CanceledAt/By.
        r.Status = WarehouseReconciliationStatus.Rejected;
        r.ApprovalComments = comments;
        r.CanceledAt = DateTime.Now;
        r.CanceledBy = userName;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
