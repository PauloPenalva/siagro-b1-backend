using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.ShipmentBilling;

public class ShipmentBillingCreateSalesInvoiceService(
    IUnitOfWork db, 
    SalesInvoicesCreateService salesInvoicesCreateService,
    DocNumbersSequenceService  docNumbersSequenceService,
    ILogger<ShipmentBillingCreateSalesInvoiceService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string username)
    {
        Validate(salesInvoice);

        try
        {
          await salesInvoicesCreateService.ExecuteAsync(salesInvoice, username);

          salesInvoice.InvoiceStatus = InvoiceStatus.Confirmed;
          await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new ApplicationException(e.Message);
        }
    }

    private static void Validate(SalesInvoice salesInvoice)
    {
        if (salesInvoice.Items.Any(i => i.SalesContractKey == null))
        {
            throw new ApplicationException("Sales Contract Key is empty.");
        }

        if (salesInvoice.SalesTransactions == null || salesInvoice.SalesTransactions.Count == 0)
        {
            throw new ApplicationException("Sales Transactions is empty.");
        }
    }
}