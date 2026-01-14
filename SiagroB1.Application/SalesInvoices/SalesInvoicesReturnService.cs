using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.SalesInvoices.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesReturnService(
    IUnitOfWork db, 
    SalesInvoicesCreateService createService,
    ILogger<SalesInvoicesReturnService> logger)
{
    public async Task<SalesInvoice> ExecuteAsync(Guid key, string userName)
    {
        var originalInvoice = await db.Context.SalesInvoices
                                  .Include(x => x.SalesTransactions)
                                  .Include(x => x.Items)
                                  .FirstOrDefaultAsync(x => x.Key == key) ??
                              throw new NotFoundException("Sales invoice not found.");

        Validate(originalInvoice);
        
        try
        {
            await db.BeginTransactionAsync();
            
            var returnInvoice = SalesInvoiceCopyFactory.CreateFrom(originalInvoice, userName);
            returnInvoice.InvoiceType = SalesInvoiceType.Return;
            returnInvoice.InvoiceStatus = InvoiceStatus.Pending;
            returnInvoice.Comments = $"Retorno do doc.saída {originalInvoice.InvoiceNumber}\n";
            returnInvoice.SalesInvoiceOriginKey = originalInvoice.Key;
            returnInvoice.Items.Clear();
            
            foreach (var item in originalInvoice.Items)
            {
                returnInvoice.AddItem(new SalesInvoiceItem
                {
                    ItemCode =  item.ItemCode,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    UnitOfMeasureCode =  item.UnitOfMeasureCode,
                    SalesInvoiceItemOriginKey = item.Key,
                    SalesContractKey = item.SalesContractKey,
                });
            }
            
            await createService.ExecuteAsync(returnInvoice, userName, CommitMode.Deferred);
            
            originalInvoice.Comments += $"Doc.Saída retornado pelo Doc.Saída {returnInvoice.InvoiceNumber}\n";
            
            await db.SaveChangesAsync();
            
            await db.CommitAsync();
            
            return returnInvoice;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError("Error: {message}", e.Message);
            throw new ApplicationException(e.Message);
        }
    }

    private static void Validate(SalesInvoice originalInvoice)
    {
        if (originalInvoice.InvoiceStatus != InvoiceStatus.Confirmed)
        {
            throw new ApplicationException("Invoice status not confirmed.");
        }
        
        if (originalInvoice.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed)
        {
            throw new ApplicationException("Invoice closed.");
        }

        if (originalInvoice.InvoiceType == SalesInvoiceType.Return)
        {
            throw new ApplicationException("Invoice type returned.");
        }
    }
}