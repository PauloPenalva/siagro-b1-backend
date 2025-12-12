using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAllocationCreateService(
    AppDbContext db, 
    StorageTransactionsGetService storageTransactionsGetService,
    PurchaseContractsGetService purchaseContractsGetService)
{
    public async Task ExecuteAsync(Guid purchaseContractKey, Guid storageTransactionKey, decimal volume, string userName)
    {

        var storageTransaction = await storageTransactionsGetService.GetByIdAsync(storageTransactionKey);
        var purchaseContract = await purchaseContractsGetService.GetByIdAsync(purchaseContractKey);
        
        var allowedTypes = new[]
        {
            StorageTransactionType.Purchase,
            StorageTransactionType.PurchaseReturn,
            StorageTransactionType.PurchaseQtyComplement,
            StorageTransactionType.PurchasePriceComplement
        };

        if (!allowedTypes.Contains(storageTransaction.TransactionType))
        {
            throw new ApplicationException("Only purchase transaction are supported.");
        }

        if (storageTransaction.TransactionStatus == StorageTransactionsStatus.Pending)
        {
            throw  new ApplicationException("The storage transaction is pending.");
        }
        
        if (volume <= 0)
        {
            throw new ApplicationException("Invalid purchase contract allocation volume.");
        }

        if (volume > storageTransaction.AvaiableVolumeToAllocate)
        {
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the delivery note.");
        }

        if (volume > purchaseContract?.AvaiableVolume)
        {
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the purchase contract.");
        }

        volume = storageTransaction.TransactionType switch
        {
            StorageTransactionType.PurchaseReturn => 0 - volume,
            StorageTransactionType.PurchaseQtyComplement => 0,
            StorageTransactionType.PurchasePriceComplement => 0,
            _ => volume
        };

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var alloc = new PurchaseContractAllocation
            {
                PurchaseContractKey = purchaseContractKey,
                StorageTransactionKey = storageTransactionKey,
                Volume = volume,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            };

            await db.PurchaseContractsAllocations.AddAsync(alloc);

            storageTransaction.AvaiableVolumeToAllocate -= decimal.Abs(volume);
            
            await db.SaveChangesAsync();
            
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw new DefaultException(e.Message);
        }
    }
}