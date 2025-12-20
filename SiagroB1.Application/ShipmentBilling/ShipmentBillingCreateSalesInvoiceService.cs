using Microsoft.Extensions.Logging;
using SiagroB1.Application.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.ShipmentBilling;

public class ShipmentBillingCreateSalesInvoiceService(
    IUnitOfWork db, 
    SalesContractsGetService salesContractsGetService,
    ILogger<ShipmentBillingCreateSalesInvoiceService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string username)
    {
        Validate(salesInvoice);

        salesInvoice.InvoiceStatus = InvoiceStatus.Confirmed;
        
        try
        {
            await db.BeginTransactionAsync();
            
            await db.Context.SalesInvoices.AddAsync(salesInvoice);
            
            await db.SaveChangesAsync();
            await db.CommitAsync();
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
        foreach (var salesInvoiceItem in salesInvoice.Items)
        {
            var salesContractKey = salesInvoiceItem.SalesContractKey;
            if (salesContractKey == null) 
                throw new ApplicationException("Sales Contract Key is null");
        }
    }
}