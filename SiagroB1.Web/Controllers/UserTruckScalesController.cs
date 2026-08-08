using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.UserTruckScales;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

[Route("odata/UserTruckScales")]
public class UserTruckScalesController(
    UserTruckScalesGetService getService,
    UserTruckScalesCreateService createService,
    UserTruckScalesUpdateService updateService,
    UserTruckScalesDeleteService deleteService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<UserTruckScale>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<UserTruckScale>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        return item == null ? NotFound() : Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] UserTruckScale entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity);
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }

        return Created(entity);
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromODataUri] Guid key, [FromBody] Delta<UserTruckScale> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var entity = await getService.GetByIdAsync(key);

        if (entity == null)
            return NotFound();

        try
        {
            patch.Patch(entity);

            await updateService.ExecuteAsync(key, entity);
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        var success = await deleteService.ExecuteAsync(key);

        return success ? NoContent() : NotFound();
    }
}
