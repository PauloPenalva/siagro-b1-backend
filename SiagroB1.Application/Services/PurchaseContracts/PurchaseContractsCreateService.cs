using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCreateService(
        AppDbContext context, 
        IBusinessPartnerService  businessPartnerService,
        IItemService itemService,
        IAgentService agentService,
        IWarehouseService warehouseService,
        DocNumberSequenceService numberSequenceService,
        IStringLocalizer<Resource> resource,
        ILogger<PurchaseContractsCreateService> logger)
{
    private static readonly TransactionCode TransactionCode = TransactionCode.PurchaseContract;
    
    public async Task<PurchaseContract> ExecuteAsync(PurchaseContract entity, string createdBy, bool isCopy = false)
    {   
        if (entity.DocNumberKey == Guid.Empty)
            throw new ApplicationException(resource["EXCEPTION_00012"]);
        
        await using var transaction = await context.Database.BeginTransactionAsync();
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
            
            entity.Code = await numberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.DeliveryLocationName = (await warehouseService.GetByIdAsync(entity.DeliveryLocationCode))?.Name;;
            entity.AgentName = (await agentService.GetByIdAsync((int) entity.AgentCode))?.Name;
            entity.Status = ContractStatus.Draft;
            
            if (entity.Type == ContractType.Fixed)
            {
                await CreatePriceFixation(entity);
            }
            else
            {
                // Contrato a fixar (PAF) não tem preço na negociação: o preço nasce das
                // fixações aprovadas pela diretoria. Zerado no servidor porque TotalStandard
                // (TotalVolume × StandardPrice) mostraria um valor fantasma que nunca foi
                // acordado — a UI desabilita o campo, mas um POST direto não passa por ela.
                entity.StandardPrice = 0;
            }
            
            await context.PurchaseContracts.AddAsync(entity);
            await context.SaveChangesAsync();
            
            await transaction.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }
    
    private async Task CreatePriceFixation(PurchaseContract entity)
    {
        var price = new PurchaseContractPriceFixation
        {
            PurchaseContract = entity,
            FixationDate = DateTime.Now.Date,
            FreightCost = entity.FreightCostStandard,
            FixationVolume = entity.TotalVolume,
            FixationPrice = entity.StandardPrice,
            // Confirmed, não InApproval: num contrato de preço fixo o preço já foi acordado
            // na negociação — esta fixação é o espelho dele, não um pedido à diretoria.
            // Nascer InApproval zeraria TotalPrice (que conta só Confirmed) e, com ele,
            // TotalTax; e ainda entulharia a fila de aprovação com item inaprovável.
            Status = PriceFixationStatus.Confirmed
        };

        await context.PurchaseContractsPriceFixations.AddAsync(price);
    }
}