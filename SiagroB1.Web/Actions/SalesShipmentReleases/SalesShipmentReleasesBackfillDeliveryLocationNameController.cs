using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesShipmentReleases;

namespace SiagroB1.Web.Actions.SalesShipmentReleases;

public class SalesShipmentReleasesBackfillDeliveryLocationNameController(
    SalesShipmentReleasesBackfillDeliveryLocationNameService service) : ODataController
{
    [HttpPost("odata/SalesShipmentReleasesBackfillDeliveryLocationName")]
    public async Task<IActionResult> BackfillAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await service.ExecuteAsync();
            return Ok(result);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
