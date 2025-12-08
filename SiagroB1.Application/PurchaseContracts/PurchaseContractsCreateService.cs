using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Constants;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsCreateService(
        AppDbContext context, 
        BusinessPartnerService  businessPartnerService,
        ItemService itemService,
        DocTypesService  docTypesService,
        ILogger<PurchaseContractsCreateService> logger)
{
    private static readonly TransactionCode TransactionCode = TransactionCode.PurchaseContract;
    
    public async Task<PurchaseContract> ExecuteAsync(PurchaseContract entity, string createdBy, bool isCopy = false)
    {
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

            var docTypeCode = entity.DocTypeCode;
            var contractNumber = await docTypesService.GetNextNumber(docTypeCode, TransactionCode);
 
            entity.Code = FormatContractNumber(contractNumber);
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.Status = ContractStatus.Draft;
            
            if (entity.Type == ContractType.Fixed)
            {
                await CreatePriceFixation(entity);
            }
            
            await context.PurchaseContracts.AddAsync(entity);
            await context.SaveChangesAsync();
            
            await docTypesService.UpdateLastNumber(docTypeCode, contractNumber, TransactionCode);
            
            await transaction.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(MessagesPtBr.CreatePurchaseContractError);
        }
    }
    
    private string FormatContractNumber(int contractNumber)
    {
        return contractNumber
            .ToString()
            .PadLeft(10, '0');
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
            Status = PriceFixationStatus.InApproval
        };

        await context.PurchaseContractsPriceFixations.AddAsync(price);
    }
}