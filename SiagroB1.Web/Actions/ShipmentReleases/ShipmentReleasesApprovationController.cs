using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesApprovationController(
    ShipmentReleasesApprovationService approvationService
    ) : ODataController
{
    [HttpPost("odata/ShipmentReleasesApprovation")]
    public async Task<ActionResult> Approvation(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var key = Guid.Parse(keyObj.ToString());
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