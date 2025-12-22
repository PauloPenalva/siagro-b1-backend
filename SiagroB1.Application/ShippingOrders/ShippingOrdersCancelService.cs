using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShippingOrders;

public class ShippingOrdersCancelService(
        AppDbContext db,
        StorageTransactionsCancelService storageTransactionsCancelService
    )
{
    public async Task<bool> ExecuteAsync(Guid key, string username)
    {
        var order = await db.ShippingOrders
                        .FirstOrDefaultAsync(x => x.Key == key) ??
                    throw new NotFoundException("Shipping order not found.");
        
        if (order.Status == ShippingOrderStatus.Invoiced)
        {
            throw new ApplicationException("Shipping order is invoiced. Please, cancel assinged invoice(s) first.");
        }
        
        if (order.Status != ShippingOrderStatus.Shipped)
        {
            throw new ApplicationException(
                "It is only possible to cancel a shipping order if the status is \"shipped\".");
        }

        var storageTrans = await db.StorageTransactions
            .FirstOrDefaultAsync(x => x.ShippingOrderKey == key); 

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            if (storageTrans != null)
            {
                await storageTransactionsCancelService.ExecuteAsync((Guid) storageTrans.Key, username, TransactionCode.ShippingOrder);
            }

            order.Status = ShippingOrderStatus.Cancelled;
            
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
}