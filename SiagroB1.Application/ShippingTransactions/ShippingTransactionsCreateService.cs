using Microsoft.Extensions.Logging;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.ShippingTransactions;

public class ShippingTransactionsCreateService(
    IUnitOfWork unitOfWork,
    StorageTransactionsCreateService storageCreateService,
    StorageTransactionsUpdateService storageUpdateService,
    StorageTransactionsConfirmedService storageConfirmedService,
    StorageTransactionsCopyService storageCopyService,
    PurchaseContractsAllocationCreateService purchaseAllocationCreateService,
    ILogger<ShippingTransactionsCreateService> logger)
{
    public async Task<ShippingTransaction> ExecuteAsync(Guid purchaseContractKey, StorageTransaction purchase, string userName)
    {
        try
        {
            await unitOfWork.BeginTransactionAsync();
            
            logger.LogInformation("Starting cross-docking for contract {ContractId}", purchaseContractKey);
            
            await storageCreateService.ExecuteAsync(
                purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
            
            await storageConfirmedService.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);
            
            await purchaseAllocationCreateService.ExecuteAsync(
                purchaseContractKey, purchase, purchase.NetWeight, userName, CommitMode.Deferred);

            var salesCreated = await storageCopyService.ExecuteAsync(
                purchase, userName, CommitMode.Deferred);
            
            salesCreated.TransactionStatus = StorageTransactionsStatus.Pending;
            salesCreated.TransactionType = StorageTransactionType.SalesShipment;
            
            //await storageUpdateService.ExecuteAsync(salesCreated.Key,  salesCreated, userName, CommitMode.Deferred);
            await storageConfirmedService.ExecuteAsync(salesCreated, userName, CommitMode.Deferred, true);

            var shipping = new ShippingTransaction
            {
                PurchaseStorageTransaction =  purchase,
                SalesStorageTransaction = salesCreated,
            };
            
            await unitOfWork.Context.ShippingTransactions.AddAsync(shipping);

            await unitOfWork.SaveChangesAsync();
        
            await unitOfWork.CommitAsync();
            
            logger.LogInformation("Cross-docking completed. ShippingId: {ShippingId}", shipping.Key);
            
            return shipping;
        }
        catch (Exception e)
        {   
            logger.LogError(e, "Cross-docking failed for contract {ContractId}", purchaseContractKey);
            await unitOfWork.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
}