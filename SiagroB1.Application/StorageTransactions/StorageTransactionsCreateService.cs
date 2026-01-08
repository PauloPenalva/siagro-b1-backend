using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCreateService(
    IUnitOfWork unitOfWork,
    DocNumberSequenceService numberSequenceService,
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
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
        TransactionCode transactionCode = TransactionCode.StorageTransaction,
        CommitMode commitMode = CommitMode.Auto)
    {
        entity.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.StorageTransaction);
        
        try
        {
            entity.Code = await numberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.WarehouseName = (await warehouseService.GetByIdAsync(entity.WarehouseCode))?.Name;
            entity.TransactionOrigin = transactionCode;
            entity.CreatedBy = userName;
            entity.CreatedAt = DateTime.Now;
            
            await unitOfWork.Context.StorageTransactions.AddAsync(entity);
            
            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();
            
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }
}