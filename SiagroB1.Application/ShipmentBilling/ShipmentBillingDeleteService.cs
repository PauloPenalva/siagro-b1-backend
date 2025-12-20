using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.ShipmentBilling;

public class ShipmentBillingDeleteService(
    IUnitOfWork db, 
    StorageTransactionsGetService storageTransactionsGetService,
    PurchaseContractsAllocationDeleteService  purchaseContractsAllocationDeleteService,
    ILogger<ShipmentBillingDeleteService> logger)
{
    public async Task ExecuteAsync(Guid key, string username)
    {
        var shipping = await db.Context.ShippingTransactions
                           .Include(x => x.PurchaseStorageTransaction)
                           .Include(x => x.SalesStorageTransaction)
                .FirstOrDefaultAsync(x => x.SalesStorageTransactionKey == key) ??
            throw new NotFoundException("Shipping not found.");

        if (shipping.SalesStorageTransaction is { TransactionStatus: StorageTransactionsStatus.Invoiced })
        {
            throw new ApplicationException("Sales transaction already invoiced.");
        }
        
        if (shipping.PurchaseStorageTransaction is { TransactionStatus: StorageTransactionsStatus.Invoiced })
        {
            throw new ApplicationException("Purchase transaction already invoiced.");
        }
        
        try
        {
            var purchaseStorageTransactionKey = shipping.PurchaseStorageTransactionKey;
            var salesStorageTransactionKey =  shipping.SalesStorageTransactionKey;
            
            await db.BeginTransactionAsync();

            shipping.PurchaseStorageTransactionKey = Guid.Empty;
            shipping.SalesStorageTransactionKey = Guid.Empty;
            
            db.Context.ShippingTransactions.Remove(shipping);

            var purchase = await storageTransactionsGetService.GetByIdAsync(purchaseStorageTransactionKey);
            var sales = await storageTransactionsGetService.GetByIdAsync(salesStorageTransactionKey);

            var purchaseContractAllocKey = await db.Context.PurchaseContractsAllocations
                .Where(x => x.StorageTransactionKey == purchaseStorageTransactionKey)
                .Select(x => x.Key)
                .FirstOrDefaultAsync();
                
            if (purchaseContractAllocKey != Guid.Empty)
                await purchaseContractsAllocationDeleteService.ExecuteAsync(purchaseContractAllocKey, username, false);

            purchase.TransactionStatus = StorageTransactionsStatus.Cancelled;
            purchase.CanceledAt = DateTime.Now;
            purchase.CanceledBy = username;
            
            sales.TransactionStatus = StorageTransactionsStatus.Cancelled;
            sales.CanceledAt = DateTime.Now;
            sales.CanceledBy = username;
            
            //db.Context.StorageTransactions.Remove(purchase);
            //db.Context.StorageTransactions.Remove(sales);
           
            await db.SaveChangesAsync();
            
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e.Message, e);
            throw new ApplicationException(e.Message);
        }
        
    }
}