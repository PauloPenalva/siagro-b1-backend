using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentBilling;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentBilling;

public class ShipmentBillingDeleteController(
    ShipmentBillingDeleteService service
    )
    : ODataController
{

    [HttpPost("odata/ShipmentBillingDeleteInvoice")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var userName = User.Identity?.Name ?? "Unknown";
            var key = (Guid) keyObj;
            
            await service.ExecuteAsync(key, userName);
            
            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
            {
                return NotFound(e.Message);
            }

            if (e is ApplicationException)
            {
                return BadRequest(e.Message);
            }
            
            return StatusCode(500, e.Message);
        }
    }
}