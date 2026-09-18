using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Anexa um documento à carga (GAC-1171). Devolve a entidade gravada porque o diálogo de registro
/// de descarga precisa da chave do anexo para ligar o ticket ao arquivo na mesma operação.
/// </summary>
public class ShipmentLoadAttachmentsCreateService(
    AppDbContext context,
    ILogger<ShipmentLoadAttachmentsCreateService> logger)
{
    public async Task<ShipmentLoadAttachment> SaveAsync(
        Guid loadKey, ShipmentLoadAttachment attachment)
    {
        try
        {
            if (!await context.ShipmentLoads.AnyAsync(x => x.Key == loadKey))
                throw new NotFoundException("Carga não encontrada.");

            attachment.ShipmentLoadKey = loadKey;
            attachment.CreatedAt ??= DateTime.Now;

            await context.AddAsync(attachment);
            await context.SaveChangesAsync();

            return attachment;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
