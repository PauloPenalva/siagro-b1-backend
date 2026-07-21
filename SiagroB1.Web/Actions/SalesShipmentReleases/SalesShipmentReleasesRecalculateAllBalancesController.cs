using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesShipmentReleases;

namespace SiagroB1.Web.Actions.SalesShipmentReleases;

public class SalesShipmentReleasesRecalculateAllBalancesController(
    SalesShipmentReleasesRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/SalesShipmentReleasesRecalculateAllBalances")]
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
