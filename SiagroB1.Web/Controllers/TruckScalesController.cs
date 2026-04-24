using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

[Route("odata/TruckScales")]
public class TruckScalesController(TruckScaleService service) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<TruckScale>> Get()
    {
        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<TruckScale>> Get([FromRoute] string key)
    {
        var item = await service.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] TruckScale entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        await service.CreateAsync(entity);

        return Created(entity);
    }

    public async Task<IActionResult> Put([FromRoute] string key, [FromBody] TruckScale entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (key != entity.Code)
        {
            return BadRequest();
        }

        try
        {
            await service.UpdateAsync(key, entity);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
    
    public async Task<IActionResult> Delete([FromRoute] string key)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var success = await service.DeleteAsync(key);

        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromODataUri] string key, [FromBody] Delta<TruckScale> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await service.GetByIdAsync(key);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await service.UpdateAsync(key, t);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }

}
