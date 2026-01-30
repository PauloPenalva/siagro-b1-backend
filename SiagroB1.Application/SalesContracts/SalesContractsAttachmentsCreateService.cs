using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsAttachmentsCreateService(
    IUnitOfWork db,
    ILogger<SalesContractsAttachmentsCreateService> logger)
{
    public async Task Save(SalesContractAttachment file)
    {
        try
        {
            db.Context.SalesContractAttachments.Add(file);

            await db.Context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message, e);
            throw new ApplicationException(e.Message);
        }
    }
}