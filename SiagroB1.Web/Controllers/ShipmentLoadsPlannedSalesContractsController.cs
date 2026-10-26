using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Contratos de venda planejados da carga (GAC-1185): somente leitura aqui; permite
/// $expand=Contract.
/// </summary>
public class ShipmentLoadsPlannedSalesContractsController(
    ShipmentLoadsPlannedContractsGetService getService)
    : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/PlannedSalesContracts")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/PlannedSalesContracts")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadPlannedSalesContract>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QuerySales(key));
    }
}
