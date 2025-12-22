using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCreateService(
    IUnitOfWork unitOfWork,
    DocNumbersSequenceService docNumberSequenceService,
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
        if (entity.DocNumberKey is null)
        {
            var docNumbers = await docNumberSequenceService.GetDocNumbersSeries(TransactionCode.StorageTransaction);
            var docNumber = docNumbers.FirstOrDefault(x => x.Default);
            if (docNumber == null)
                throw new ApplicationException("Document Number is empty or not setting default value.");

            entity.DocNumberKey = docNumber.Key;
        }
        
        try
        {
            var docNumber = await docNumberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            var currentNumber = docNumber.NextNumber;
                
            entity.Code = DocNumbersSequenceService
                .FormatNumber(currentNumber, int.Parse(docNumber.NumberSize), docNumber.Prefix, docNumber.Suffix);
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.TransactionOrigin = transactionCode;
            entity.CreatedBy = userName;
            entity.CreatedAt = DateTime.Now;
            
            await unitOfWork.Context.StorageTransactions.AddAsync(entity);
            
            await docNumberSequenceService.UpdateLastNumber((Guid) entity.DocNumberKey, currentNumber);
            
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