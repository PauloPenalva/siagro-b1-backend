using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAttachmentsGetService(
    IUnitOfWork db, 
    ILogger<PurchaseContractsAttachmentsGetService> logger)
{
    public IEnumerable<PurchaseContractAttachmentsDto> ListAttachmentsByContract(Guid purchaseContractKey)
    {
        return db.Context.PurchaseContractAttachments
            .Where(x => x.PurchaseContractKey == purchaseContractKey)
            .AsNoTracking()
            .Select(x => new PurchaseContractAttachmentsDto
            {
                Key = x.Key,
                Description = x.Description,
                FileName = x.FileName,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt!
            })
            .ToList();
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