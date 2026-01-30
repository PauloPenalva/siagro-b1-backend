using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAttachmentsCreateService(
    IUnitOfWork db, 
    ILogger<PurchaseContractsAttachmentsCreateService>  logger)
{
    public async Task Save(PurchaseContractAttachment file)
    {
        try
        {
            db.Context.PurchaseContractAttachments.Add(file);

            await db.Context.SaveChangesAsync();
        }
        catch (Exception e)
        {
           logger.LogError(e.Message, e);
           throw new ApplicationException(e.Message);
        }
    }
    
    
}