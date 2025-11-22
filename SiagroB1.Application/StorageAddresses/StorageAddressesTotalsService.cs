using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesTotalsService(AppDbContext db)
{
    public async Task<StorageAddressTotalsDto> ExecuteAsync(Guid key)
    {
        var address = await db.StorageAddresses
                          .Include(x => x.Transactions)
                          .FirstOrDefaultAsync(x => x.Key == key) ??
                      throw new NotFoundException("Storage address not found.");

        return new StorageAddressTotalsDto
        {
            Balance = address.Balance,
            TotalReceipt = address.TotalReceipt,
            TotalShipment = address.TotalShipment,
            TotalQualityLoss = address.TotalQualityLoss
        };
    }
}