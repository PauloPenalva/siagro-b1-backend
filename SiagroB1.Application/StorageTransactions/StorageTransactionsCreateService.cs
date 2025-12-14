using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCreateService(
    IUnitOfWork unitOfWork,
    DocTypesService  docTypesService,
    BusinessPartnerService  businessPartnerService,
    ItemService itemService,
    ILogger<StorageTransactionsCreateService> logger)
{
    public async Task<StorageTransaction> ExecuteAsyncWithTransaction(
            StorageTransaction entity, 
            string userName, 
            TransactionCode transactionCode = TransactionCode.StorageTransaction
        )
    {
        try
        {
            await unitOfWork.BeginTransactionAsync();
            
            await ExecuteAsync(entity, userName, transactionCode);
            
            await unitOfWork.CommitAsync();
            
            return entity;
        }
        catch (Exception ex)
        {
            await unitOfWork.RollbackAsync();
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }
    
    public async Task<StorageTransaction> ExecuteAsync(
        StorageTransaction entity, 
        string userName, 
        TransactionCode transactionCode = TransactionCode.StorageTransaction
    )
    {
        try
        {
            var currentNumber = await docTypesService.GetNextNumber(entity.DocTypeCode, TransactionCode.StorageTransaction);

            entity.Code = FormatCode(currentNumber);
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.TransactionOrigin = transactionCode;
            entity.CreatedBy = userName;
            entity.CreatedAt = DateTime.Now;
            
            await unitOfWork.Context.StorageTransactions.AddAsync(entity);
            
            await docTypesService.UpdateLastNumber(entity.DocTypeCode, currentNumber, TransactionCode.StorageTransaction);
            
            await unitOfWork.SaveChangesAsync();
            
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }
    
    private static string FormatCode(int currentNumber)
    {
        return currentNumber
            .ToString()
            .PadLeft(10, '0');
    }
}