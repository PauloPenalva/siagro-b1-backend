using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesUpdateService(AppDbContext context, ILogger<StorageAddressesUpdateService> logger)
{
    public async Task<StorageAddress?> ExecuteAsync(string code, StorageAddress entity, string userName)
    {
        var existingAddress = await context.StorageAddresses
                                  .FirstOrDefaultAsync(x => x.Code == code) ??
                              throw new NotFoundException("Storage address not found.");

        if (existingAddress.TransactionOrigin != TransactionCode.StorageAddress)
        {
            throw new ApplicationException("This record was created by another transaction. It cannot be updated.");
        }
        
        try
        {
            context.Entry(existingAddress).CurrentValues.SetValues(entity);

            // SaveAsync changes
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}