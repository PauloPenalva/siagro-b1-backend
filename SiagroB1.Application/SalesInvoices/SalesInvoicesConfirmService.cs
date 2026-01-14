using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesConfirmService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices
                          .Include(x => x.SalesTransactions)
                          .Include(x => x.Items)
                          .FirstOrDefaultAsync(x => x.Key == key) ??
                              throw new NotFoundException("Sales invoice not found.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new ApplicationException("Invoice is not pending.");
        
        await db.BeginTransactionAsync();
        
        if (invoice.InvoiceType == SalesInvoiceType.Return)
        {
            var totalQtyReturned = decimal.Zero;
            
            foreach (var item in invoice.Items)
            {
                ValidateLineItemBalance(item);
                
                item.DeliveredQuantity = item.Quantity;
                item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

                totalQtyReturned += item.Quantity;
            }
            
            var originInvoice = await db.Context.SalesInvoices
                                    .Include(e => e.SalesTransactions)
                                    .FirstOrDefaultAsync(x => x.Key == invoice.SalesInvoiceOriginKey) ??
                                throw new KeyNotFoundException($"Key {key} not found");

            if (originInvoice.InvoiceStatus == InvoiceStatus.Cancelled)
            {
                throw new ApplicationException($"Sales invoice origin is in canceled status.");
            }
                
            var salesTransactionsKeys = originInvoice.SalesTransactions?
                .Select(x => x.Key)
                .ToList() ?? [];
            
            originInvoice.SalesTransactions?.Clear();
            
            foreach (var salesTransactionsKey in salesTransactionsKeys)
            {
                var salesTransaction = await db.Context.StorageTransactions
                    .FirstOrDefaultAsync(x => x.Key == salesTransactionsKey);

                if (salesTransaction != null)
                {
                    salesTransaction.TransactionStatus = StorageTransactionsStatus.Confirmed;
                    salesTransaction.InvoiceQty = 0;
                    salesTransaction.InvoiceSerie = string.Empty;
                    salesTransaction.InvoiceNumber = string.Empty;
                    salesTransaction.UpdatedAt = DateTime.Now;
                    salesTransaction.UpdatedBy = userName;
                    salesTransaction.SalesInvoiceKey = null;
                    salesTransaction.GrossWeight = totalQtyReturned;
                }
            }
        }

        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.ApprovedBy = userName;
        invoice.ApprovedAt =  DateTime.Now;
        
        await db.SaveChangesAsync();
        await db.CommitAsync();
    }

    private void ValidateLineItemBalance(SalesInvoiceItem item)
    {
        var totalOriginal = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.Key == item.SalesInvoiceItemOriginKey)
            .Select(x => x.Quantity)
            .SingleOrDefault();

        if (totalOriginal <= 0)
        {
            throw new ApplicationException("Original invoice item not found.");
        }

        var totalIncoming = db.Context.SalesInvoicesItems
            .Where(x =>
                x.SalesInvoice.InvoiceType == SalesInvoiceType.Return &&
                x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled &&
                x.SalesInvoiceItemOriginKey == item.SalesInvoiceItemOriginKey &&
                x.Key != item.Key)
            .Sum(x => x.Quantity);

        if (totalIncoming + item.Quantity > totalOriginal)
        {
            throw new ApplicationException(
                "Returned quantity exceeds the original invoice item quantity."
            );
        }
    }
}