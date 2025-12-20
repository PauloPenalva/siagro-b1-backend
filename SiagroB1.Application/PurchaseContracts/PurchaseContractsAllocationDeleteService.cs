using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAllocationDeleteService(
    IUnitOfWork db, 
    StorageTransactionsGetService storageTransactionsGetService,
    PurchaseContractsGetService purchaseContractsGetService)
{
    public async Task ExecuteAsync(Guid key, string userName, bool useTransaction = true)
    {
        var alloc = await db.Context.PurchaseContractsAllocations
                        .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Purchase contract allocation not found.");
        
        var storageTransaction = await storageTransactionsGetService.GetByIdAsync(alloc.StorageTransactionKey);
        
        try
        {
            if (useTransaction)
                await db.BeginTransactionAsync();
            
            db.Context.PurchaseContractsAllocations.Remove(alloc);
            
            storageTransaction.AvaiableVolumeToAllocate += decimal.Abs(alloc.Volume);
            
            await db.SaveChangesAsync();
            
            if (useTransaction)
                await db.CommitAsync();
        }
        catch (Exception e)
        {
            if (useTransaction)
                await db.RollbackAsync();
            
            throw new DefaultException(e.Message);
        }
    }
}