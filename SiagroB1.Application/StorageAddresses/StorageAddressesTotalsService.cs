using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesTotalsService(IUnitOfWork db)
{
    public async Task<StorageAddressTotalsDto> ExecuteAsync(string code)
    {
        var address = await db.Context.StorageAddresses
                          .Include(x => x.Transactions)
                          .FirstOrDefaultAsync(x => x.Code == code) ??
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