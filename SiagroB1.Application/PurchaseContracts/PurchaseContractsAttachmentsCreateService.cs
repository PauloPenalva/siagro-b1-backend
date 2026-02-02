using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAttachmentsCreateService(
    IUnitOfWork db, 
    ILogger<PurchaseContractsAttachmentsCreateService>  logger)
{
    public async Task SaveAsync(Guid contractKey, PurchaseContractAttachment attachment)
    {
        try
        {
            var contract = await db.Context.PurchaseContracts
                .FirstOrDefaultAsync(x => x.Key == contractKey)
                ?? throw new ApplicationException($"Contrato de compra não encontrado.");
            
            contract.AddAttachment(attachment);

            await db.Context.SaveChangesAsync();
        }
        catch (Exception e)
        {
           logger.LogError(e.Message, e);
           throw new ApplicationException(e.Message);
        }
    }
    
    
}