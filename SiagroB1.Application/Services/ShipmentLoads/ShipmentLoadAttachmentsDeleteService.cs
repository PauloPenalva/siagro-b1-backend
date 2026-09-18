using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Exclui um anexo da carga (GAC-1171), desde que nenhum ticket de descarga aponte para ele.
/// </summary>
public class ShipmentLoadAttachmentsDeleteService(
    AppDbContext context,
    ILogger<ShipmentLoadAttachmentsDeleteService> logger)
{
    public async Task ExecuteAsync(Guid attachmentKey, string userName)
    {
        try
        {
            var attachment = await context.ShipmentLoadsAttachments
                .FirstOrDefaultAsync(x => x.Key == attachmentKey)
                ?? throw new NotFoundException("Anexo não encontrado.");

            // Guard antes da FK: sem ele o banco devolve erro 547 e o usuário vê um 500 de corpo
            // vazio, sem saber que o problema é o vínculo com o ticket.
            if (await context.ShipmentLoadsDischarges.AnyAsync(x => x.AttachmentKey == attachmentKey))
                throw new DefaultException(
                    "Este anexo está vinculado a um registro de descarga. " +
                    "Exclua o registro de descarga antes de excluir o anexo.");

            context.ShipmentLoadsAttachments.Remove(attachment);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Anexo {Key} da carga excluído por {User}.", attachmentKey, userName);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
