using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsAttachmentsGetService(
    IUnitOfWork db,
    ILogger<SalesContractsAttachmentsGetService> logger)
{
    public IQueryable<SalesContractAttachment> QueryAll(Guid key)
    {
        return db.Context.SalesContractAttachments
            .Where(x => x.Key == key)
            .AsNoTracking();
    }
    
    public async Task<SalesContractAttachment?> GetByKey(Guid key)
    {
        try
        {
            return await db.Context.SalesContractAttachments.FindAsync(key);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}