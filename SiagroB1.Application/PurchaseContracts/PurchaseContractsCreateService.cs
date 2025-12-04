using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Constants;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsCreateService(AppDbContext context, ILogger<PurchaseContractsCreateService> logger)
{
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

            var contractNumber = await GetPurchaseContractNumber(entity.DocTypeCode);
            entity.Code = contractNumber
                                .ToString()
                                .PadLeft(10, '0');
            if (!isCopy) 
                entity.Status = ContractStatus.InApproval;
            
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
            await context.PurchaseContracts.AddAsync(entity);
            
            await UpdateDocType(entity.DocTypeCode, contractNumber);

            if (entity.Type == ContractType.Fixed)
            {
                await CreatePriceFixation(entity);
            }
            
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            
            if (ex is DefaultException)
            {
                throw;
            }

            logger.LogError(ex, ex.Message);
            throw new DefaultException(MessagesPtBr.CreatePurchaseContractError);
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
            Status = PriceFixationStatus.InApproval
        };

        await context.PurchaseContractsPriceFixations.AddAsync(price);
    }

    private async Task<int> GetPurchaseContractNumber(string? docTypeCode)
    {
        if (docTypeCode == null)
        {
            throw new ApplicationException("DocType Code not informed.");
        }

        var nextNumber = await context.DocTypes
                             .AsNoTracking()
                             .Where(x => x.Code == docTypeCode && x.TransactionCode == TransactionCode.PurchaseContract)
                             .Select(x => x.NextNumber)
                             .FirstOrDefaultAsync();

        return nextNumber;
    }
    
    
    private async Task UpdateDocType(string docTypeCode, int contractNumber)
    {
        if (docTypeCode == null)
        {
            throw new ApplicationException("DocType Code not informed.");
        }

        var docType = await context.DocTypes
                          .Where(x => x.Code == docTypeCode && 
                                      x.TransactionCode == TransactionCode.PurchaseContract)
                          .FirstOrDefaultAsync() ??
                      throw new NotFoundException("DocType not found.");
        docType.LastNumber = contractNumber;
        docType.NextNumber = ++contractNumber;
    }
}