using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ShipmentReleases;

public class ShipmentReleasesApprovationController(
    ShipmentReleasesApprovationService approvationService
    ) : ODataController
{
    [HttpPost("odata/ShipmentReleasesApprovation({key:guid})")]
    public async Task<ActionResult> Approvation([FromRoute] Guid key)
    {
        try
        {
            await approvationService.ExecuteAsync(key);
        
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