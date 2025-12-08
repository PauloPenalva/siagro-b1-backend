using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsCreateService(
    AppDbContext context, 
    BusinessPartnerService  businessPartnerService,
    ItemService itemService,
    DocTypesService  docTypesService,
    ILogger<SalesContractsCreateService> logger)
{
    private static readonly TransactionCode TransactionCode = TransactionCode.SalesContract;

    public async Task<SalesContract> ExecuteAsync(SalesContract entity, string createdBy)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var docTypeCode = entity.DocTypeCode;
            var contractNumber = await docTypesService.GetNextNumber(docTypeCode, TransactionCode);
            
            entity.Code = FormatContractNumber( contractNumber);
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = createdBy;
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.Status = ContractStatus.Draft;
            
            await context.SalesContracts.AddAsync(entity);
            await context.SaveChangesAsync();
            
            await docTypesService.UpdateLastNumber(docTypeCode, contractNumber, TransactionCode);
            
            await transaction.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, ex.Message);
            throw new ApplicationException("Unable to create sales contract.");
        }
    }

    private string FormatContractNumber(int contractNumber)
    {
        return contractNumber
            .ToString()
            .PadLeft(10, '0');
    }
}