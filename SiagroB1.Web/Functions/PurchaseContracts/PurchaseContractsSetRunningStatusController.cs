
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsSetRunningStatusController(
    PurchaseContractsSetRunnigStatusService service
    ) : ODataController
{
    [HttpPost("odata/PurchaseContractsSetRunningStatus({key:guid})")]
    public async Task<ActionResult> SetRunningStatus([FromODataUri] Guid key)
    {
        try
        {
            await service.ExecuteAsync(key);
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