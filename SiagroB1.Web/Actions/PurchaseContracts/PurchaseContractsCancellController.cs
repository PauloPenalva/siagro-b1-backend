using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsCancelController(
    PurchaseContractsCancelService service
    ) : ODataController
{
    [HttpPost]
    [Route("odata/PurchaseContracts({key:guid})/Cancel")]
    public async Task<ActionResult> Cancel([FromRoute] Guid key)
    {
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            
            await service.Cancel(key, userName);
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