using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocTypes;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesCreateService(
    IUnitOfWork db,
    BusinessPartnerService businessPartnerService,
    ItemService itemService,
    DocTypesService  docTypesService,
    ILogger<SalesInvoicesCreateService> logger)
{
    public async Task ExecuteAsync(SalesInvoice salesInvoice, string userName)
    {
        if (salesInvoice.Items.Count == 0)
            throw new ApplicationException("Items can not be empty.");
        
        var docTypeCode = salesInvoice.DocTypeCode;
        if (docTypeCode is null)
            throw new ApplicationException("DocTypeCode can not be null.");
        
        try
        {
            var invoiceNumber = await docTypesService.GetNextNumber(docTypeCode, TransactionCode.SalesInvoice);
            var invoiceSeries = await docTypesService.GetSerie(docTypeCode, TransactionCode.SalesInvoice);
            
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
            
            await db.Context.SalesInvoices.AddAsync(salesInvoice);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
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