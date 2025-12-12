using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsAllocationDeleteService(
    AppDbContext db, 
    StorageTransactionsGetService storageTransactionsGetService,
    PurchaseContractsGetService purchaseContractsGetService)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var alloc = await db.PurchaseContractsAllocations
                        .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Purchase contract allocation not found.");
        
        var storageTransaction = await storageTransactionsGetService.GetByIdAsync(alloc.StorageTransactionKey);
        
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            db.PurchaseContractsAllocations.Remove(alloc);
            
            storageTransaction.AvaiableVolumeToAllocate += decimal.Abs(alloc.Volume);
            
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