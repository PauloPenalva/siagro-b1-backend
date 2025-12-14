using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore; 
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsCopyService(IUnitOfWork unitOfWork,StorageTransactionsCreateService createService)
{
    public async Task<StorageTransaction> ExecuteAsync(Guid key, string userName) 
    {
        var original = await unitOfWork.Context.StorageTransactions
            .AsNoTracking()
            .Include(x => x.QualityInspections)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                               throw new NotFoundException("Storage transaction not found.");

        
        var clone = JsonSerializer.Deserialize<StorageTransaction>(
            JsonSerializer.Serialize(original, 
                new JsonSerializerOptions 
                { 
                    ReferenceHandler = ReferenceHandler.IgnoreCycles 
                })) ??  throw new ApplicationException("Error on copying purchase contract.");

        clone.Key = null;
        clone.RowId = 0;
        clone.TransactionDate = DateTime.Now.Date;
        clone.TransactionStatus = StorageTransactionsStatus.Pending;
        clone.CreatedAt = DateTime.Now.Date;
        clone.CreatedBy = userName;
       
        foreach (var inspection in clone.QualityInspections)
        {
            inspection.Key = null;
            inspection.StorageTransaction = clone;
        }
        
        await createService.ExecuteAsync(clone, userName);

        return clone;
    }
}