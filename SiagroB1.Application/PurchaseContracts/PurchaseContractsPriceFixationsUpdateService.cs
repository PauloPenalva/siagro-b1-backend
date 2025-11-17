using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsPriceFixationsUpdateService(
    AppDbContext context, ILogger<PurchaseContractsPriceFixationsCreateService> logger)
{
    public async Task<PurchaseContractPriceFixation?> ExecuteAsync(Guid parenteKey, Guid associationKey, PurchaseContractPriceFixation associationEntity)
    {
        try
        {
            if (!ExistingPurchaseContract(parenteKey))
            {
                throw new NotFoundException("Purchase Contract not found");
            }
            
            var existingEntity = await context.PurchaseContractsPriceFixations.FindAsync(associationKey)
                ?? throw new NotFoundException("Price Fixation not found");

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);
            await context.SaveChangesAsync();
            
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    private bool ExistingPurchaseContract(Guid key)
    {
        return  context.PurchaseContracts.Any(x => x.Key == key);
    }
}