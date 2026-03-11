using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.OwnershipTransfers;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.OwnershipTransfers;

public class OwnershipTransfersCancelController(
    OwnershipTransfersCancelService service
    ) : ODataController
{
    [HttpPost("odata/OwnershipTransfersCancel")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
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
            
            await service.ExecuteAsync(key,userName);
            
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