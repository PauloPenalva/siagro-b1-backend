using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesCreateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    DocNumberSequenceService numberSequenceService,
    ILogger<SalesInvoicesCreateService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string userName)
    {
        if (salesInvoice.Items.Count == 0)
            throw new ApplicationException("Items can not be empty.");

        salesInvoice.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.SalesInvoice);
        
        try
        {
            salesInvoice.CreatedAt = DateTime.Now;
            salesInvoice.CreatedBy = userName;
            salesInvoice.InvoiceNumber = await numberSequenceService.GetDocNumber((Guid) salesInvoice.DocNumberKey);
            salesInvoice.InvoiceStatus = InvoiceStatus.Pending;
            salesInvoice.CardName = (await businessPartnerService.GetByIdAsync(salesInvoice.CardCode))?.CardName;
            salesInvoice.TruckingCompanyName = 
                salesInvoice.TruckingCompanyCode != null 
                    ? (await businessPartnerService.GetByIdAsync(salesInvoice.TruckingCompanyCode))?.CardName
                    : string.Empty;

            foreach (var item in salesInvoice.Items)
            {
                item.ItemName = (await itemService.GetByIdAsync(item.ItemCode))?.ItemName;
            }

            var salesTransactions = new List<Guid>();
            
            foreach (var salesTransaction in salesInvoice.SalesTransactions)
            {
                salesTransactions.Add(salesTransaction.Key);
            }
            
            salesInvoice.SalesTransactions.Clear();
            
            await db.Context.SalesInvoices.AddAsync(salesInvoice);

            foreach (var transactionKey in salesTransactions)
            {
                var existingTransaction = await db.Context.StorageTransactions
                    .FirstOrDefaultAsync(x => x.Key == transactionKey) ??
                                          throw new ApplicationException($"Transaction {transactionKey} not found.");
                
                existingTransaction.InvoiceNumber = salesInvoice.InvoiceNumber;
                existingTransaction.InvoiceQty = existingTransaction.GrossWeight;
                existingTransaction.SalesInvoiceKey = salesInvoice.Key;
                existingTransaction.TransactionStatus = StorageTransactionsStatus.Invoiced;
            }
            
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError("Error: {message}", e.Message);
            throw new ApplicationException(e.Message);
        }
    }
    
    private static string FormatInvoiceNumber(int invoiceNumber)
    {
        return invoiceNumber
            .ToString()
            .PadLeft(9, '0');
    }
}