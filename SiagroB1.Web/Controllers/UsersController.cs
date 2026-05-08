using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Users;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class UsersController(
    UsersCreateService createService,
    UsersUpdateService updateService,
    UsersGetService getService,
    UsersDeleteService deleteService
    ) 
    : ODataController
{
    [EnableQuery]
    [HttpGet("odata/Users")]
    public ActionResult<IEnumerable<User>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    [HttpGet("odata/Users({id})")]
    [HttpGet("odata/Users/{id}")]
    public async Task<ActionResult<User>> Get([FromRoute] Guid id)
    {
        var item = await getService.GetByIdAsync(id);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpPost("odata/Users")]
    public async Task<IActionResult> Post([FromBody] User entity)
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
            if (ex is DefaultException or ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }
    
    [HttpPut("odata/Users({id})")]
    [HttpPut("odata/Users/{id}")]
    public async Task<IActionResult> Put([FromRoute] Guid id, [FromBody] User entity)
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
    
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Users({id})")]
    [AcceptVerbs("PATCH", "MERGE", Route = "odata/Users/{id}")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid id, [FromBody] Delta<User> patch)
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
    
    [HttpDelete("odata/Users({id})")]
    [HttpDelete("odata/Users/{id}")]
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
}