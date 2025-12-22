using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCancelService(AppDbContext db)
{
    public async Task ExecuteAsync(Guid key, string username, TransactionCode transactionCode = TransactionCode.StorageTransaction)
    {
        var doc = await db.StorageTransactions
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new NotFoundException("Storage transaction not found.");

        if (doc.TransactionOrigin != transactionCode)
        {
            var msg =
                "This storage transaction is created by another transaction. It cannot be canceled using this method.\n" +
                "Transaction Origin: " + doc.TransactionOrigin;
            
            throw new ApplicationException(msg);
        }
        
        if (doc.TransactionStatus == StorageTransactionsStatus.Invoiced)
        {
            throw new ApplicationException("Storage transaction is invoiced. Please, cancel assinged invoice first.");
        }

        try
        {
            doc.TransactionStatus = StorageTransactionsStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
}