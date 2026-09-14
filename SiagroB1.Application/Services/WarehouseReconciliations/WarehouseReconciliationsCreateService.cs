using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsCreateService(
    IUnitOfWork db,
    DocNumberSequenceService numbers,
    WarehouseReconciliationsDescriptionService descriptions,
    WarehouseReconciliationsGuardService guard,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(WarehouseReconciliation r, string userName)
    {
        // O cliente não escolhe status, parceiro nem carimbos de fluxo.
        r.Status = WarehouseReconciliationStatus.Draft;
        r.ReferenceDate = r.ReferenceDate.Date;
        r.SentAt = null;
        r.SentBy = null;
        r.ApprovedAt = null;
        r.ApprovedBy = null;
        r.CanceledAt = null;
        r.CanceledBy = null;
        r.ApprovalComments = null;
        r.CancellationReason = null;
        r.StorageTransactionKey = null;

        await guard.EnsureCanPersistAsync(r);

        r.DocNumberKey ??= await numbers.GetKeyByTransactionCode(TransactionCode.WarehouseReconciliation);
        r.Code = await numbers.GetDocNumber(r.DocNumberKey.Value);
        await descriptions.FillAsync(r);
        await guard.RefreshSnapshotAsync(r);

        r.CreatedAt = DateTime.Now;
        r.CreatedBy = userName;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.Context.WarehouseReconciliations.AddAsync(r);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // O guard consultou o banco ANTES deste SaveChanges: duas criações concorrentes para
            // o mesmo armazém+produto passam as duas por ele e só colidem aqui, no índice único
            // filtrado (IX_WAREHOUSE_RECONCILIATIONS_OpenPerWarehouseItem). Sem este catch o
            // perdedor da corrida vira um 500 cru em vez da mensagem de negócio de sempre.
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ALREADY_OPEN"].Value);
        }
    }
}
