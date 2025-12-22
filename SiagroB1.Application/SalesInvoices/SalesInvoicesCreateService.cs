using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesCreateService(
    IUnitOfWork db,
    BusinessPartnerService businessPartnerService,
    ItemService itemService,
    DocNumbersSequenceService docNumberSequenceService,
    ILogger<SalesInvoicesCreateService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string userName)
    {
        if (salesInvoice.Items.Count == 0)
            throw new ApplicationException("Items can not be empty.");
        
        if (salesInvoice.DocNumberKey is null)
        {
            var docNumbers = await docNumberSequenceService.GetDocNumbersSeries(TransactionCode.SalesInvoice);
            var docNumber = docNumbers.FirstOrDefault(x => x.Default);
            if (docNumber == null)
                throw new ApplicationException("Document Number is empty or not setting default value.");

            salesInvoice.DocNumberKey = docNumber.Key;
        }
        
        try
        {
            await db.BeginTransactionAsync();
            
            var docNumber = await docNumberSequenceService.GetDocNumber((Guid) salesInvoice.DocNumberKey);
            
            var invoiceNumber = docNumber.NextNumber;

            salesInvoice.InvoiceNumber = DocNumbersSequenceService
                .FormatNumber(invoiceNumber, int.Parse(docNumber.NumberSize), docNumber.Prefix, docNumber.Suffix);
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

            await docNumberSequenceService.UpdateLastNumber((Guid) salesInvoice.DocNumberKey, invoiceNumber);
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