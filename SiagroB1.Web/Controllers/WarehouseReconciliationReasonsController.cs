using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WarehouseReconciliationReasonsController(
    WarehouseReconciliationReasonsCreateService createService,
    WarehouseReconciliationReasonsUpdateService updateService,
    WarehouseReconciliationReasonsDeleteService deleteService,
    WarehouseReconciliationReasonsGetService getService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<WarehouseReconciliationReason>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<WarehouseReconciliationReason>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);
        return item == null ? NotFound() : Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] WarehouseReconciliationReason entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(entity);
        }
        catch (ApplicationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<WarehouseReconciliationReason> patch)
    {
        var current = await getService.GetByIdAsync(key);
        if (current == null)
            return NotFound();

        patch.Patch(current);

        try
        {
            await updateService.ExecuteAsync(key, current, User.Identity?.Name ?? "Unknown");
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ApplicationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ApplicationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
