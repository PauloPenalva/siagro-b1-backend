using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesItemsCreateService(
    IUnitOfWork db,
    IItemService itemService,
    ILogger<SalesInvoicesItemsCreateService> logger)
{
    public async Task ExecuteAsync(SalesInvoiceItem salesInvoiceItem, string userName)
    {
        try
        {
            salesInvoiceItem.ItemName = (await itemService.GetByIdAsync(salesInvoiceItem.ItemCode))?.ItemName; 
            
            await db.Context.SalesInvoicesItems.AddAsync(salesInvoiceItem);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError("Error: {message}", e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}