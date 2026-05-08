using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Roles;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class RolesController(
    RolesCreateService createService,
    RolesUpdateService updateService,
    RolesDeleteService deleteService,
    RolesGetService getService
    ) 
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/Roles")]
    public ActionResult<IEnumerable<Role>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    [HttpGet("odata/Roles({id})")]
    [HttpGet("odata/Roles/{id}")]
    public async Task<ActionResult<Role>> Get([FromRoute] Guid id)
    {
        var item = await getService.GetByIdAsync(id);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    public async Task<IActionResult> Post([FromBody] Role entity)
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

    public async Task<IActionResult> Put([FromRoute] Guid id, [FromBody] Role entity)
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

    [HttpDelete("odata/Roles({id})")]
    [HttpDelete("odata/Roles/{id}")]
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
    public virtual async Task<IActionResult> Patch([FromRoute] Guid id, [FromBody] Delta<Role> patch)
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