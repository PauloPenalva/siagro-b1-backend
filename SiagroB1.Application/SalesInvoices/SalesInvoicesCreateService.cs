using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesCreateService(
    IUnitOfWork db,
    BusinessPartnerService businessPartnerService,
    ItemService itemService,
    StorageTransactionsGetService  storageTransactionsGetService,
    DocTypesService  docTypesService,
    ILogger<SalesInvoicesCreateService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string userName)
    {
        if (salesInvoice.Items.Count == 0)
            throw new ApplicationException("Items can not be empty.");
        
        try
        {
            await db.BeginTransactionAsync();
            
            var docType = await docTypesService.GetDocType(TransactionCode.SalesInvoice);

            var invoiceNumber = docType.NextNumber;
            var invoiceSeries = docType.Serie;
            
            salesInvoice.InvoiceNumber = FormatInvoiceNumber(invoiceNumber);
            salesInvoice.InvoiceSeries = invoiceSeries;
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
                existingTransaction.InvoiceSerie  = salesInvoice.InvoiceSeries;
                existingTransaction.InvoiceQty = existingTransaction.GrossWeight;
                existingTransaction.SalesInvoiceKey = salesInvoice.Key;
                existingTransaction.TransactionStatus = StorageTransactionsStatus.Invoiced;
            }
            
            await db.SaveChangesAsync();

            await docTypesService.UpdateLastNumber(docType.Code, invoiceNumber, TransactionCode.SalesInvoice);
            await db.CommitAsync();
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