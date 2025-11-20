using Microsoft.Extensions.Logging;
using SiagroB1.Application.Constants;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsCreateService(AppDbContext context, ILogger<PurchaseContractsCreateService> logger)
{
    public async Task<PurchaseContract> ExecuteAsync(PurchaseContract entity, string createdBy)
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
            
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
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

            logger.LogError(ex, ex.Message);
            throw new DefaultException(MessagesPtBr.CreatePurchaseContractError);
        }
    }    
}