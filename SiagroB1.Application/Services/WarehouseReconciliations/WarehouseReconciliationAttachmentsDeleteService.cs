using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsDeleteService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource)
{
    public async Task Delete(Guid key)
    {
        var attachment = await db.Context.WarehouseReconciliationAttachments.FirstOrDefaultAsync(x => x.Key == key)
                         ?? throw new NotFoundException("Anexo não encontrado.");

        var status = await db.Context.WarehouseReconciliations
            .AsNoTracking()
            .Where(x => x.Key == attachment.WarehouseReconciliationKey)
            .Select(x => (WarehouseReconciliationStatus?)x.Status)
            .FirstOrDefaultAsync();

        // Mesma trava do upload: anexo de conferência já decidida é registro histórico.
        if (status is not (WarehouseReconciliationStatus.Draft or WarehouseReconciliationStatus.InApproval))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ATTACHMENTS_LOCKED"].Value);

        db.Context.WarehouseReconciliationAttachments.Remove(attachment);
        await db.SaveChangesAsync();
    }
}
