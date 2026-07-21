using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesShipmentReleases;

public class SalesShipmentReleasesPauseController(
    SalesShipmentReleasesPauseService service) : ODataController
{
    [HttpPost("odata/SalesShipmentReleasesPause")]
    public async Task<IActionResult> PauseAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            var key = Guid.Parse(keyObj.ToString());
            await service.ExecuteAsync(key);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
