using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsAttachmentsGetService(
    IUnitOfWork db,
    ILogger<SalesContractsAttachmentsGetService> logger)
{
    public IEnumerable<SalesContractAttachmentsDto> ListAttachmentsByContract(Guid salesContractKey)
    {
        return db.Context.SalesContractAttachments
            .Where(x => x.SalesContractKey == salesContractKey)
            .AsNoTracking()
            .Select(x => new SalesContractAttachmentsDto
            {
                Key = x.Key,
                Description = x.Description,
                FileName = x.FileName,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt!
            })
            .ToList();
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