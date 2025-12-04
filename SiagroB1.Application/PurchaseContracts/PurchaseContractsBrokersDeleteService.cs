using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsBrokersDeleteService(
    AppDbContext context, ILogger<PurchaseContractsBrokersDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid associationKey)
    {
        try
        {
            var existingEntity = await context.PurchaseContractsBrokers.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Broker not found");

            return await Delete(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }
    
    public async Task<bool> ExecuteAsync(Guid parentKey, Guid associationKey)
    {
        try
        {
            if (!ExistingPurchaseContract(parentKey))
            {
                throw new NotFoundException("Purchase Contract not found");
            }

            return await Delete(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }

    private async Task<bool> Delete(Guid associationKey)
    {
        try
        {
           
            var existingEntity = await context.PurchaseContractsBrokers.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Broker not found.");

            context.PurchaseContractsBrokers.Remove(existingEntity);
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