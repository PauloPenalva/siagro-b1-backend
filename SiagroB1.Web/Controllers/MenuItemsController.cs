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
    public ActionResult<IEnumerable<MenuItem>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<MenuItem>> Get([FromRoute] string key)
    {
        var item = await getService.GetByIdAsync(key);

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

    public async Task<IActionResult> Put([FromRoute] string key, [FromBody] MenuItem entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(key, entity);
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

    public async Task<IActionResult> Delete([FromRoute] string key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
            
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
    public async Task<IActionResult> Patch([FromRoute] string key, [FromBody] Delta<MenuItem> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var t = await getService.GetByIdAsync(key);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(key, t);
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