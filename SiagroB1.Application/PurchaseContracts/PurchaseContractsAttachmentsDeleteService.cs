using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAttachmentsDeleteService(
    IUnitOfWork db, 
    ILogger<PurchaseContractsAttachmentsDeleteService>  logger)
{
    public async Task Delete(Guid key)
    {
        var attachment = await db.Context.PurchaseContractAttachments
            .FirstOrDefaultAsync(x => x.Key == key) ??
                         throw new NotFoundException($"Anexo não encontrado.");
        try
        {
            db.Context.PurchaseContractAttachments.Remove(attachment);
            await db.Context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}