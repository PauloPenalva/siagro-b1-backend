using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsPriceFixationsDeleteService(
    AppDbContext context, ILogger<PurchaseContractsPriceFixationsCreateService> logger)
{
    public async Task<bool> ExecuteAsync(Guid parentKey, Guid associationKey)
    {
        try
        {
            if (!ExistingPurchaseContract(parentKey))
            {
                throw new NotFoundException("Purchase Contract not found");
            }
            
            var existingEntity = await context.PurchaseContractsPriceFixations.FindAsync(associationKey)
                ?? throw new NotFoundException("Price Fixation not found");

            context.PurchaseContractsPriceFixations.Remove(existingEntity);
            await context.SaveChangesAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }

    private bool ExistingPurchaseContract(Guid key)
    {
        return context.PurchaseContracts.Any(x => x.Key == key);
    }
}