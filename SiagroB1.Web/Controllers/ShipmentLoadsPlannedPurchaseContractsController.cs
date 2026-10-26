using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Contratos de compra planejados da carga (GAC-1185): somente leitura aqui; permite
/// $expand=Contract.
/// </summary>
public class ShipmentLoadsPlannedPurchaseContractsController(
    ShipmentLoadsPlannedContractsGetService getService)
    : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/PlannedPurchaseContracts")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/PlannedPurchaseContracts")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadPlannedPurchaseContract>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryPurchase(key));
    }
}
