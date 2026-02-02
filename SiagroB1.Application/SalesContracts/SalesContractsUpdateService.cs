using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsUpdateService(
    AppDbContext context, 
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    IAgentService agentService,
    ILogger<SalesContractsUpdateService> logger)
{
    public async Task<SalesContract?> ExecuteAsync(Guid key, SalesContract entity, string userName)
    {
        var existingEntity = await context.Set<SalesContract>()
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
        
        if (existingEntity.Status != ContractStatus.Draft)
        {
            throw new ApplicationException("You can only edit a sales contract if its status is draft.");
        }
        
        try
        {
            context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // SaveAsync changes
            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.AgentName = (await agentService.GetByIdAsync((int) entity.AgentCode))?.Name;
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}