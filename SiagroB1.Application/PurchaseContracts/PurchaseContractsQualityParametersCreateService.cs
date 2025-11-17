using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsQualityParametersCreateService(
    AppDbContext context, ILogger<PurchaseContractsQualityParametersCreateService> logger)
{
    public async Task<PurchaseContractQualityParameter> ExecuteAsync(Guid puchaseContractKey, PurchaseContractQualityParameter associationEntity)
    {
        try
        {
            var existingEntity = await context.PurchaseContracts.FindAsync(puchaseContractKey)
                ?? throw new NotFoundException("Purchase contract not found");
            
            associationEntity.PurchaseContract = existingEntity;
            await context.AddAsync(associationEntity);
            
            await context.SaveChangesAsync();
            
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}