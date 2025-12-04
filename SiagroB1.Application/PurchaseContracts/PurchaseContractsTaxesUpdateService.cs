using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsTaxesUpdateService(
    AppDbContext context, ILogger<PurchaseContractsTaxesUpdateService> logger)
{
    public async Task<PurchaseContractTax?> ExecuteAsync(Guid associationKey, PurchaseContractTax associationEntity)
    {
        try
        {
            var existingEntity = await context.PurchaseContractsTaxes.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Tax not found");

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
    
    public async Task<PurchaseContractTax?> ExecuteAsync(Guid parenteKey, Guid associationKey, PurchaseContractTax associationEntity)
    {
        try
        {
            if (!ExistingPurchaseContract(parenteKey))
            {
                throw new NotFoundException("Purchase Contract not found");
            }
            
            var existingEntity = await context.PurchaseContractsTaxes.FindAsync(associationKey)
                ?? throw new NotFoundException("Tax not found");

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