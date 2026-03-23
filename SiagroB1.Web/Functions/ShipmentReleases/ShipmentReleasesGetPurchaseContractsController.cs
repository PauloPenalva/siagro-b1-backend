using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.ShipmentReleases;

public class ShipmentReleasesGetPurchaseContractsController(ShipmentReleasesPurchaseContractsService service)
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/ShipmentReleasesGetPurchaseContracts(ItemCode={itemCode},WarehouseCode={warehouseCode})")]
    public async Task<ActionResult<ICollection<ShipmentRelesesPurchaseContractsResponseDto>>> GetAsync(
        [FromRoute] string itemCode,
        [FromRoute] string warehouseCode)
    {
        return Ok(await service.ExecuteAsync(itemCode, warehouseCode));
    }
}