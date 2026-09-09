using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Log de alterações da carga: somente leitura. Quem escreve é
/// <c>ShipmentLoadsChangeLogService</c>, sempre no mesmo SaveChanges da alteração que descreve.
/// </summary>
public class ShipmentLoadsChangeLogsController(
    ShipmentLoadsChangeLogsGetService getService)
    : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/ChangeLogs")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadChangeLog>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
