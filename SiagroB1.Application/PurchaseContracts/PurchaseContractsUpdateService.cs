using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsUpdateService(
    AppDbContext context, 
    BusinessPartnerService businessPartnerService,
    ItemService itemService,
    ILogger<PurchaseContractsUpdateService> logger
    )
{
    public async Task<PurchaseContract?> ExecuteAsync(Guid key, PurchaseContract entity, string userName)
    {
        try
        {
            var existingEntity = await context.Set<PurchaseContract>()
                .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");

            if (existingEntity.Status != ContractStatus.Draft)
            {
                throw new ApplicationException("You can only edit a purchase contract if its status is draft.");
            }

            context.Entry(existingEntity).CurrentValues.SetValues(entity);
            
            if (existingEntity.Type == ContractType.Fixed)
            {
                await UpdatePriceFixation(existingEntity);
            }
            
            // Save changes
            existingEntity.Status = ContractStatus.Draft;
            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(key))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                logger.Log(LogLevel.Error, "Failed to update entity.");
                throw new DefaultException("Error updating entity due to concurrency issues.");
            }
        }

        return entity;
    }

    private async Task UpdatePriceFixation(PurchaseContract entity)
    {  
        var price = await context.PurchaseContractsPriceFixations
            .FirstOrDefaultAsync(pf => pf.PurchaseContractKey == entity.Key) ??
                    throw new KeyNotFoundException("Price fixation not found.");
        
        price.FreightCost = entity.FreightCostStandard;
        price.FixationVolume = entity.TotalVolume;
        price.FixationPrice = entity.StandardPrice;
        price.Status = PriceFixationStatus.InApproval;
    }

    private bool EntityExists(Guid key)
    {
        return context.Set<PurchaseContract>().Any(e => e.Key == key);
    }
}