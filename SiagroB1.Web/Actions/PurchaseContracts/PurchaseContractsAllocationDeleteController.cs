using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsAllocationDeleteController(PurchaseContractsAllocationDeleteService service)
: ODataController
{
    [HttpPost("odata/PurchaseContractsDeleteAllocation")]
    public async Task<IActionResult> DeleteAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) )
            {
                return BadRequest("Missing required parameters");
            }
            
            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());
            
            await service.ExecuteWithTransactionAsync(key, userName);
            
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }

            if (e is ApplicationException or DefaultException or FormatException)
            {
                return BadRequest(e.Message);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}