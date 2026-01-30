using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAttachmentsGetService(
    IUnitOfWork db, 
    ILogger<PurchaseContractsAttachmentsGetService>  logger)
{
    public IQueryable<PurchaseContractAttachment> QueryAll(Guid key)
    {
        return db.Context.PurchaseContractAttachments
            .Where(x => x.Key == key)
            .AsNoTracking();
    }
    
    public async Task<PurchaseContractAttachment?> GetByKey(Guid key)
    {
        try
        {
            return await db.Context.PurchaseContractAttachments.FindAsync(key);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}