using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Actions.StorageInvoices;

public class StorageInvoiceClosingController(
    IStorageInvoiceClosingService service
    ) : ODataController
{
    [HttpPost("odata/StorageInvoiceClosing")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("DocNumberKey", out var docNumberKeyObj) || 
                !parameters.TryGetValue("StorageAddressCode", out var storageAddressCodeObj) ||
                !parameters.TryGetValue("PeriodStart", out var periodStartObj) ||
                !parameters.TryGetValue("PeriodEnd", out var periodEndObj) ||
                !parameters.TryGetValue("Notes", out var notesObj) ||
                !parameters.TryGetValue("IncludeUnpricedItems", out var includeUnpricedItemsObj) ||
                !parameters.TryGetValue("ClosingDate", out var closingDateObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var docNumberKey =  Guid.Parse(docNumberKeyObj.ToString());
            var storageAddressCode =  storageAddressCodeObj.ToString();
            var periodStart =  DateTime.Parse(periodStartObj.ToString());
            var periodEnd =  DateTime.Parse(periodEndObj.ToString());
            var closingDate =  DateTime.Parse(closingDateObj.ToString());
            var notes = notesObj?.ToString() ?? string.Empty;
            var includeUnpricedItems =  Boolean.Parse(includeUnpricedItemsObj.ToString());
            var userName = User.Identity?.Name ?? "Unknown";

            var invoice = await service.CloseAsync(new StorageInvoiceCloseRequest
            {
                DocNumberKey = docNumberKey,
                StorageAddressCode = storageAddressCode,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                ClosingDate = closingDate,
                Notes = notes,
                IncludeUnpricedItems = includeUnpricedItems
            }, userName);
            
            return Ok(new StorageInvoiceCloseResponse
            {
                Key = invoice.Key,
            });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }
            
            return BadRequest(e.Message);
        }
        
    }
    
}