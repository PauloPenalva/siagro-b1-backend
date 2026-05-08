using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.MenuItem;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class MenuItemsController(
    MenuItemsCreateService createService,
    MenuItemsUpdateService updateService,
    MenuItemsDeleteService deleteService,
    MenuItemsGetService getService
    ) 
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/MenuItems")]
    public ActionResult<IEnumerable<MenuItem>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    [HttpGet("odata/MenuItems({id})")]
    [HttpGet("odata/MenuItems/{id}")]
    public async Task<ActionResult<MenuItem>> Get([FromRoute] Guid id)
    {
        var item = await getService.GetByIdAsync(id);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    public async Task<IActionResult> Post([FromBody] MenuItem entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            await createService.ExecuteAsync(entity);

            return Created(entity);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }

    public async Task<IActionResult> Put([FromRoute] Guid id, [FromBody] MenuItem entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(id, entity);
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception ex)
        {
            if (ex is ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    [HttpDelete("odata/MenuItems({id})")]
    [HttpDelete("odata/MenuItems/{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        try
        {
            await deleteService.ExecuteAsync(id);
            
            return NoContent();
        }
        catch (Exception ex)
        {
            return ex switch
            {
                NotFoundException => NotFound(ex.Message),
                ApplicationException => BadRequest(ex.Message),
                _ => StatusCode(500, ex.Message)
            };
        }
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid id, [FromBody] Delta<MenuItem> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await getService.GetByIdAsync(id);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(id, t);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException or ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}