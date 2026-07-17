using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesRecalculateBalanceController(
    ShipmentReleasesRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesRecalculateBalance")]
    public async Task<IActionResult> RecalculateAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            var key = Guid.Parse(keyObj.ToString());
            var result = await service.ExecuteAsync(key);
            return Ok(result);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
