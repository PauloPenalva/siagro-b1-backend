using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsAttachmentsCreateService(
    IUnitOfWork db,
    ILogger<SalesContractsAttachmentsCreateService> logger)
{
    public async Task SaveAsync(Guid contractKey, SalesContractAttachment attachment)
    {
        try
        {
            var contract = await db.Context.SalesContracts
                               .FirstOrDefaultAsync(x => x.Key == contractKey)
                           ?? throw new ApplicationException($"Contrato de venda não encontrado.");
            
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