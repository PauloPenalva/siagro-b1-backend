using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class ShipmentLoadMovementsController(ShipmentLoadsGetService getService) : ODataController
{
    [HttpGet("odata/ShipmentLoadMovements")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadMovement>> Get()
    {
        return Ok(getService.QueryMovements());
    }

    [HttpGet("odata/ShipmentLoadMovements({key:guid})")]
    [HttpGet("odata/ShipmentLoadMovements/{key:guid}")]
    [EnableQuery]
    public ActionResult<ShipmentLoadMovement> Get([FromRoute] Guid key)
    {
        var item = getService.QueryMovements().FirstOrDefault(x => x.Key == key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
}
