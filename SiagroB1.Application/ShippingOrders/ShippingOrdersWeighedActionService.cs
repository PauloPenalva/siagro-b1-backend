using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShippingOrders;

public class ShippingOrdersWeighedActionService(AppDbContext db)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var order = await db.ShippingOrders
                        .FirstOrDefaultAsync(x => x.Key == key) ??
                    throw new NotFoundException("Shipping order not found.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await CreateStorageTransaction(order);
            
            order.Status = ShippingOrderStatus.Weighed;
            
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private async Task CreateStorageTransaction(ShippingOrder order)
    {
        var st = new StorageTransaction
        {
            StorageAddressKey = order.StorageAddressKey,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionsStatus = StorageTransactionsStatus.Waiting,
            NetWeight = order.Volume,
            TransactionOrigin = TransactionCode.ShippingOrder,
            ShippingOrderKey = order.Key
        };

        db.StorageTransactions.Add(st);
        await db.SaveChangesAsync();
    }
}