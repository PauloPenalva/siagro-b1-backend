using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesApprovationController(
    ShipmentReleasesApprovationService approvationService
    ) : ODataController
{
    [HttpPost]
    [Route("/odata/ShipmentReleases({key:guid})/Approvation")]
    public async Task<ActionResult> Approvation([FromRoute] Guid key)
    {
        try
        {
            var usarName = User.Identity?.Name ?? "Unknown";
            await approvationService.ExecuteAsync(key, usarName);
        
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