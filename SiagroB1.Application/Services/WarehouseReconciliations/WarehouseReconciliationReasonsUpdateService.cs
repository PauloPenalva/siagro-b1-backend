using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsUpdateService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, WarehouseReconciliationReason input, string userName)
    {
        var reason = await db.Context.WarehouseReconciliationReasons.FirstOrDefaultAsync(x => x.Key == key)
                     ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_REASON_NOT_FOUND"].Value);

        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        var description = (input.Description ?? string.Empty).Trim();

        if (code.Length == 0 || description.Length == 0)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS"].Value);

        if (await db.Context.WarehouseReconciliationReasons.AnyAsync(x => x.Key != key && x.Code == code))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE"].Value);

        // Atribuição explícita: um PATCH não pode reescrever auditoria.
        reason.Code = code;
        reason.Description = description;
        reason.Active = input.Active;
        reason.UpdatedAt = DateTime.Now;
        reason.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
