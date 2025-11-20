using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsUpdateService(AppDbContext context, ILogger<StorageTransactionsUpdateService> logger)
{
    public Task<StorageTransaction?> ExecuteAsync(Guid key, StorageTransaction entity, string userName)
    {
        throw new NotImplementedException();
    }
}