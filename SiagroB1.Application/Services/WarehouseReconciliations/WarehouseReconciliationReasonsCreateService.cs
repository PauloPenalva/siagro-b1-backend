using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsCreateService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(WarehouseReconciliationReason reason, string userName)
    {
        reason.Code = (reason.Code ?? string.Empty).Trim().ToUpperInvariant();
        reason.Description = (reason.Description ?? string.Empty).Trim();

        if (reason.Code.Length == 0 || reason.Description.Length == 0)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS"].Value);

        if (await db.Context.WarehouseReconciliationReasons.AnyAsync(x => x.Code == reason.Code))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE"].Value);

        reason.Active = true;
        reason.CreatedAt = DateTime.Now;
        reason.CreatedBy = userName;
        reason.UpdatedAt = DateTime.Now;
        reason.UpdatedBy = userName;

        await db.Context.WarehouseReconciliationReasons.AddAsync(reason);
        await db.SaveChangesAsync();
    }
}
