using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsUpdateService(
    IUnitOfWork db,
    WarehouseReconciliationsDescriptionService descriptions,
    WarehouseReconciliationsGuardService guard,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, WarehouseReconciliation input, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.Draft)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_DRAFT"].Value);

        // Atribuição explícita dos campos editáveis: um PATCH não reescreve status nem auditoria.
        r.BranchCode = input.BranchCode ?? r.BranchCode;
        r.WarehouseCode = input.WarehouseCode;
        r.ItemCode = input.ItemCode;
        r.UnitOfMeasureCode = input.UnitOfMeasureCode;
        r.ReferenceDate = input.ReferenceDate.Date;
        r.ReportedBalance = input.ReportedBalance;
        r.ReasonKey = input.ReasonKey;
        r.Comments = input.Comments;

        await guard.EnsureCanPersistAsync(r);
        await descriptions.FillAsync(r);
        await guard.RefreshSnapshotAsync(r);

        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Mesma corrida do Create: o guard viu o banco livre antes deste SaveChanges, e só o
            // índice único filtrado barra a colisão.
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ALREADY_OPEN"].Value);
        }
    }
}
