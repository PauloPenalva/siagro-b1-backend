using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsGetService(IUnitOfWork unitOfWork,ILogger<StorageTransactionsGetService> logger)
{
    public async Task<StorageTransaction> GetByIdAsync(Guid key)
    {
        StorageTransaction? storageTransaction;

        try
        {
            storageTransaction = await unitOfWork.Context.StorageTransactions
                .Include(x => x.DocNumber)
                .Include(x => x.QualityInspections)
                .ThenInclude(q => q.QualityAttrib)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }

        return storageTransaction ?? throw new NotFoundException("Storage transaction not found.");
    }

    public IQueryable<StorageTransaction> QueryAll()
    {
        return unitOfWork.Context.StorageTransactions.AsNoTracking();
    }
}