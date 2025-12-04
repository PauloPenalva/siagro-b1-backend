using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsQualityParametersDeleteService(
    AppDbContext context, ILogger<PurchaseContractsQualityParametersDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid associationKey)
    {
        try
        {
            var existingEntity = await context.PurchaseContractsQualityParameters.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Quality parameter not found");

            context.PurchaseContractsQualityParameters.Remove(existingEntity);
            await context.SaveChangesAsync();
            
            return true;
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
            
            var existingEntity = await context.PurchaseContractsQualityParameters.FindAsync(associationKey)
                ?? throw new NotFoundException("Quality parameter not found");

            context.PurchaseContractsQualityParameters.Remove(existingEntity);
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