using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsAttachmentsDeleteService(
    IUnitOfWork db,
    ILogger<SalesContractsAttachmentsDeleteService> logger)
{
    public async Task Delete(Guid key)
    {
        var attachment = await db.Context.SalesContractAttachments
                             .FirstOrDefaultAsync(x => x.Key == key) ??
                         throw new NotFoundException($"Anexo não encontrado.");
        try
        {
            db.Context.SalesContractAttachments.Remove(attachment);
            await db.Context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}