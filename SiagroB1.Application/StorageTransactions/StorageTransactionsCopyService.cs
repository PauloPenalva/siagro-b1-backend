using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.StorageTransactions.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCopyService(
    IUnitOfWork unitOfWork,
    DocNumberSequenceService numberSequenceService,
    StorageTransactionsCreateService createService)
{
    public async Task<StorageTransaction> ExecuteAsync(Guid key, string userName, CommitMode commitMode = CommitMode.Auto) 
    {
        var original = await unitOfWork.Context.StorageTransactions
            .AsNoTracking()
            .Include(x => x.QualityInspections)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                               throw new NotFoundException("Storage transaction not found.");
        
        var clone = StorageTransactionCopyFactory.CreateFrom(original, userName);
        
        await createService.ExecuteAsync(clone, userName, TransactionCode.StorageTransaction, commitMode);

        return clone;
    }
    
    public async Task<StorageTransaction> ExecuteAsync(StorageTransaction original, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var clone = StorageTransactionCopyFactory.CreateFrom(original, userName);
        
        await createService.ExecuteAsync(clone, userName, TransactionCode.StorageTransaction, commitMode);

        return clone;
    }
}