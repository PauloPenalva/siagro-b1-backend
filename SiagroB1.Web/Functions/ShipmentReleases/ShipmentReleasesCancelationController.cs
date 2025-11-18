using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ShipmentReleases;

public class ShipmentReleasesCancelationController(
    ShipmentReleasesCancelationService cancelationService
    ) : ODataController
{
    [HttpPost("odata/ShipmentReleasesCancelation({key:guid})")]
    public async Task<ActionResult> Cancelation([FromRoute] Guid key)
    {
        try
        {
            await cancelationService.ExecuteAsync(key);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound();
            }
            
            return BadRequest(e.Message);
        }
       
    }
}