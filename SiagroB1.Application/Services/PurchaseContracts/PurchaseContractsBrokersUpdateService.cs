using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsBrokersUpdateService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService,
    ILogger<PurchaseContractsBrokersUpdateService> logger)
{
    public async Task<PurchaseContractBroker?> ExecuteAsync(Guid associationKey, PurchaseContractBroker associationEntity)
    {
        try
        {
            var existingEntity = await context.PurchaseContractsBrokers.FindAsync(associationKey)
                                 ?? throw new NotFoundException("Broker not found.");

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);
            // Depois do SetValues e em `existingEntity`, senão a gravação se perde.
            existingEntity.CardName =
                (await businessPartnerService.GetByIdAsync(associationEntity.CardCode))?.CardName;

            await context.SaveChangesAsync();
            
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
    
    public async Task<PurchaseContractBroker?> ExecuteAsync(Guid parenteKey, Guid associationKey, PurchaseContractBroker associationEntity)
    {
        try
        {
            if (!ExistingPurchaseContract(parenteKey))
            {
                throw new NotFoundException("Purchase Contract not found");
            }
            
            var existingEntity = await context.PurchaseContractsBrokers.FindAsync(associationKey)
                ?? throw new NotFoundException("Broker not found");

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);
            // Depois do SetValues e em `existingEntity`, senão a gravação se perde.
            existingEntity.CardName =
                (await businessPartnerService.GetByIdAsync(associationEntity.CardCode))?.CardName;

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