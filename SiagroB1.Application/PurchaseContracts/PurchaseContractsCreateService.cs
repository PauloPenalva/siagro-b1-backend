using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsCreateService(AppDbContext context, ILogger<PurchaseContractsCreateService> logger)
{
    public async Task<PurchaseContract> ExecuteAsync(PurchaseContract entity)
    {
        try
        {
            foreach (var fixation in entity.PriceFixations)
            {
                fixation.PurchaseContract = entity;
            }
            
            foreach (var tax in entity.Taxes)
            {
                tax.PurchaseContract = entity;
            }

            foreach (var parameter in entity.QualityParameters)
            {
                parameter.PurchaseContract = entity;
            }
            
            await context.PurchaseContracts.AddAsync(entity);
            await context.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                throw;
            }

            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }    
}