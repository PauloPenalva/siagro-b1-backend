using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsGetService(IUnitOfWork db)
{
    // Projeção sem FileData: a lista não arrasta o binário.
    public IEnumerable<WarehouseReconciliationAttachmentDto> ListByReconciliation(Guid reconciliationKey) =>
        db.Context.WarehouseReconciliationAttachments
            .AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == reconciliationKey)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new WarehouseReconciliationAttachmentDto
            {
                Key = x.Key,
                Description = x.Description,
                FileName = x.FileName,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt,
            })
            .ToList();

    public Task<WarehouseReconciliationAttachment?> GetByKey(Guid key) =>
        db.Context.WarehouseReconciliationAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
