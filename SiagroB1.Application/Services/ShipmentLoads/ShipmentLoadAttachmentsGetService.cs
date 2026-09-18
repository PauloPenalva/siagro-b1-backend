using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

public class ShipmentLoadAttachmentsGetService(AppDbContext context)
{
    // Projeção sem FileData: a lista não arrasta o binário.
    public IEnumerable<ShipmentLoadAttachmentDto> ListByLoad(Guid loadKey) =>
        context.ShipmentLoadsAttachments
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == loadKey)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ShipmentLoadAttachmentDto
            {
                Key = x.Key,
                AttachmentType = x.AttachmentType,
                Description = x.Description,
                FileName = x.FileName,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt,
            })
            .ToList();

    public Task<ShipmentLoadAttachment?> GetByKey(Guid key) =>
        context.ShipmentLoadsAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
