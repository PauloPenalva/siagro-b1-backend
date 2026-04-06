using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShippingTransactions;

public class ShippingTransactionsCreateService(
    IUnitOfWork unitOfWork,
    StorageTransactionsCreateService storageCreateService,
    StorageTransactionsConfirmedService storageConfirmedService,
    StorageTransactionsCopyService storageCopyService,
    PurchaseContractsAllocationCreateService purchaseAllocationCreateService)
{
    public async Task<ShippingTransaction> ExecuteAsync(Guid purchaseContractKey, StorageTransaction purchase, string userName)
    {
        try
        {
            await unitOfWork.BeginTransactionAsync();
            
            await storageCreateService.ExecuteAsync(
                purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
            
            await storageConfirmedService.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);
            
            await purchaseAllocationCreateService.ExecuteAsync(
                purchaseContractKey, purchase, purchase.NetWeight, userName, CommitMode.Deferred);

            var salesCreated = await storageCopyService.ExecuteAsync(
                purchase, userName, CommitMode.Deferred);
            
            salesCreated.TransactionStatus = StorageTransactionsStatus.Pending;
            salesCreated.TransactionType = StorageTransactionType.SalesShipment;
            
            await storageConfirmedService.ExecuteAsync(salesCreated, userName, CommitMode.Deferred, true);

            var shipping = new ShippingTransaction
            {
                PurchaseStorageTransaction =  purchase,
                SalesStorageTransaction = salesCreated,
            };
            
            await unitOfWork.Context.ShippingTransactions.AddAsync(shipping);

            await unitOfWork.SaveChangesAsync();
        
            await unitOfWork.CommitAsync();
            
            return shipping;
        }
        catch (Exception e)
        {   
            await unitOfWork.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
}