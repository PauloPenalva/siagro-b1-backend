using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.SalesShipmentReleases;

public class SalesShipmentReleasesGetAvailableController(
    SalesShipmentReleasesGetAvailableService service)
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/SalesShipmentReleasesGetAvailable(ItemCode={itemCode})")]
    public ActionResult<IEnumerable<SalesShipmentReleaseAvailableDto>> Get([FromRoute] string itemCode)
    {
        return Ok(service.Query(itemCode));
    }
}
