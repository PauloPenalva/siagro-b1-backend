using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesDeleteService(AppDbContext context, ILogger<StorageAddressesDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var address = await context.StorageAddresses
                           .Include(x => x.Transactions)
                           .FirstOrDefaultAsync(x => x.Key == key) ??
                       throw new NotFoundException("Storage Address not found.");

        if (address.TransactionOrigin != TransactionCode.StorageAddress)
        {
            throw new ApplicationException("This record was created by another transaction. It cannot be deleted.");
        }
        
        context.StorageAddresses.Remove(address);
        await context.SaveChangesAsync();
        return true;
    }
}