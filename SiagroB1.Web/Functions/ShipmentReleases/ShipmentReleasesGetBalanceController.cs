using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.ShipmentReleases;

public class ShipmentReleasesGetBalanceController(ShipmentReleasesBalanceService service)
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/ShipmentReleasesGetBalance(ItemCode={itemCode})")]
    public async Task<ActionResult<ICollection<ShipmentRelesesBalanceResponseDto>>> GetAsync([FromRoute] string itemCode)
    {
        return Ok(await service.ExecuteAsync(itemCode));
    }
}