using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsSendApprovalService(
    IUnitOfWork db,
    WarehouseReconciliationsGuardService guard,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.Draft)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_DRAFT"].Value);

        await guard.EnsureCanPersistAsync(r);

        // O aprovador vê números reais, não os do dia em que o rascunho foi digitado.
        await guard.RefreshSnapshotAsync(r);

        if (r.Difference == decimal.Zero)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE"].Value);

        // Revisão 17/09 (spec §9): só perda, e a distribuição entre liberações precisa fechar.
        await guard.EnsureLossDistributionAsync(r);

        r.Status = WarehouseReconciliationStatus.InApproval;
        r.SentAt = DateTime.Now;
        r.SentBy = userName;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
