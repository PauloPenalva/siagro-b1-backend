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
                                  .Include(x => x.Items)
                                  .FirstOrDefaultAsync(x => x.Key == key) ??
                              throw new NotFoundException("Sales invoice not found.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
        {
            throw new ApplicationException("Invoice is not pending.");
        }
        
        if (invoice.InvoiceType == SalesInvoiceType.Return)
        {
            foreach (var item in invoice.Items)
            {
                ValidateLineItemBalance(item);
                
                item.DeliveredQuantity = item.Quantity;
                item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
            }
        }

        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.ApprovedBy = userName;
        invoice.ApprovedAt =  DateTime.Now;
        
        await db.SaveChangesAsync();
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