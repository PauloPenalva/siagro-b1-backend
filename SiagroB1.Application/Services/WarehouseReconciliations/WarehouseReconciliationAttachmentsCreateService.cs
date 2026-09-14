using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsCreateService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource)
{
    public async Task SaveAsync(Guid reconciliationKey, WarehouseReconciliationAttachment attachment)
    {
        var reconciliation = await db.Context.WarehouseReconciliations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == reconciliationKey)
            ?? throw new NotFoundException("Conferência de saldo não encontrada.");

        // Anexo é evidência do extrato usado na decisão: depois de Aprovada/Rejeitada/Cancelada
        // ele vira o registro do que foi julgado e não pode mais mudar por baixo do pé.
        EnsureUnlocked(reconciliation.Status);

        attachment.WarehouseReconciliationKey = reconciliationKey;
        attachment.CreatedAt ??= DateTime.Now;

        await db.Context.WarehouseReconciliationAttachments.AddAsync(attachment);
        await db.SaveChangesAsync();
    }

    private void EnsureUnlocked(WarehouseReconciliationStatus status)
    {
        if (status is not (WarehouseReconciliationStatus.Draft or WarehouseReconciliationStatus.InApproval))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ATTACHMENTS_LOCKED"].Value);
    }
}
