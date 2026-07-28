using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsUpdateService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    IAgentService agentService,
    ContractNotificationOutboxService notificationOutbox,
    ILogger<PurchaseContractsUpdateService> logger
    )
{
    public async Task<PurchaseContract?> ExecuteAsync(Guid key, PurchaseContract entity, string userName)
    {
        var existingEntity = await context.Set<PurchaseContract>()
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");

        if (existingEntity.Status != ContractStatus.Draft)
        {
            throw new ApplicationException("You can only edit a purchase contract if its status is draft.");
        }
        
        try
        {
            context.Entry(existingEntity).CurrentValues.SetValues(entity);
            
            if (existingEntity.Type == ContractType.Fixed)
            {
                await UpdatePriceFixation(existingEntity);
            }
            
            // SaveAsync changes
            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            // Precisa ser `existingEntity`: o SetValues acima já copiou `entity`, então
            // gravar em `entity` aqui não chega ao registro rastreado e a coluna fica velha.
            existingEntity.DeliveryLocationName = (await warehouseService.GetByIdAsync(entity.DeliveryLocationCode))?.Name;
            existingEntity.AgentName = (await agentService.GetByIdAsync((int) entity.AgentCode))?.Name;

            // O diff tem de sair AQUI: depois do SaveChanges o EF sincroniza OriginalValues com
            // o que foi gravado e a comparação sai vazia. Sem campo notificável alterado não há
            // notificação — uma edição que só mexeu em coluna derivada não vira mensagem.
            var changes = ContractHeaderDiffBuilder.Build(
                context.Entry(existingEntity), NotificationDocumentType.PurchaseContract);

            if (changes.Count > 0)
                notificationOutbox.Register(
                    existingEntity, NotificationEventType.HeaderUpdated, userName, changes);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
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
        // Confirmed pelo mesmo motivo da criação: é o espelho do preço já acordado.
        // Devolver para InApproval aqui zeraria TotalPrice/TotalTax a cada edição.
        price.Status = PriceFixationStatus.Confirmed;
    }
}