using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesRecalculateAllBalancesController(
    ShipmentReleasesRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesRecalculateAllBalances")]
    public async Task<IActionResult> RecalculateAllAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await service.ExecuteAllAsync();
            return Ok(result);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
