using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCreateService(
    AppDbContext context, 
    DocTypesService  docTypesService,
    BusinessPartnerService  businessPartnerService,
    ItemService itemService,
    PurchaseContractService purchaseContractService,
    ILogger<StorageTransactionsCreateService> logger)
{
    public async Task<StorageTransaction> ExecuteAsync(
            StorageTransaction entity, 
            string userName, 
            TransactionCode transactionCode = TransactionCode.StorageTransaction
        )
    {
        await Validate(entity);
        
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var currentNumber = await docTypesService.GetNextNumber(entity.DocTypeCode, TransactionCode.StorageTransaction);

            entity.Code = FormatCode(currentNumber);
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.TransactionOrigin = transactionCode;
            
            await context.StorageTransactions.AddAsync(entity);
            await context.SaveChangesAsync();
            
            await docTypesService.UpdateLastNumber(entity.DocTypeCode, currentNumber, TransactionCode.StorageTransaction);
            
            await transaction.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }

    private async Task Validate(StorageTransaction entity)
    {
        // if (entity.PurchaseContractKey != null)
        // {
        //     var purchaseContract = await purchaseContractService.GetByIdAsync((Guid) entity.PurchaseContractKey);
        //     
        //     if (purchaseContract != null)
        //     {
        //         if (purchaseContract.Status != ContractStatus.Approved)
        //             throw new DefaultException("The purchase contract is not approved.");
        //             
        //         if (purchaseContract.CardCode != entity.CardCode)
        //             throw new DefaultException("Invalid card code");
        //         
        //         if (purchaseContract.ItemCode != entity.ItemCode)
        //             throw new DefaultException("Invalid item code");
        //     }
        // }
    }
    
    private static string FormatCode(int currentNumber)
    {
        return currentNumber
            .ToString()
            .PadLeft(10, '0');
    }
}