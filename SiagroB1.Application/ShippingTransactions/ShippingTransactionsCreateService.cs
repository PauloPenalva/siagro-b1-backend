using Microsoft.Extensions.Logging;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

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
            
            await storageCreateService.ExecuteAsync(purchase, userName);
            await storageConfirmedService.ExecuteAsync((Guid) purchase.Key, userName);
            
            await purchaseAllocationCreateService.ExecuteAsync(
                purchaseContractKey, (Guid) purchase.Key, purchase.NetWeight, userName);

            var sales = await storageCopyService.ExecuteAsync((Guid) purchase.Key, userName);
            sales.TransactionStatus = StorageTransactionsStatus.Pending;
            sales.TransactionType = StorageTransactionType.SalesShipment;
            
            await storageUpdateService.ExecuteAsync((Guid) sales.Key,  sales, userName);
            await storageConfirmedService.ExecuteAsync((Guid) sales.Key, userName);

            var shipping = new ShippingTransaction
            {
                PurchaseStorageTransaction =  purchase,
                SalesStorageTransaction = sales,
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