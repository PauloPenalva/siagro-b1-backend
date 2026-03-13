using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Actions.StorageInvoices;

public class StorageInvoiceCancellationController(
    IStorageInvoiceCancellationService service
    ) : ODataController
{
    [HttpPost("odata/StorageInvoiceCancellation")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("StorageInvoiceKey", out var storageInvoiceKeyObj) || 
                !parameters.TryGetValue("Reason", out var reasonObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var storageInvoiceKey =  Guid.Parse(storageInvoiceKeyObj.ToString());
            var reason =  reasonObj.ToString();
            var userName = User.Identity?.Name ?? "Unknown";

            await service.CancelAsync(new StorageInvoiceCancelRequest
            {
                StorageInvoiceKey = storageInvoiceKey,
                Reason = reason,
            }, userName);
            
            return Ok();
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