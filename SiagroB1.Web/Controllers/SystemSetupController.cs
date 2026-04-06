using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class SystemSetupController(SystemSetupService service) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/SystemSetup")]
    public ActionResult<IEnumerable<DocNumber>> Get()
    {
        return Ok(service.QueryAll());
    }
    
    [EnableQuery]
    [HttpGet("odata/SystemSetup({key})")]
    [HttpGet("odata/SystemSetup/{key}")]
    public async Task<ActionResult<EnvironmentSetup>> Get([FromRoute] string key)
    {
        var item = await service.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [HttpPost("odata/SystemSetup")]
    public async Task<IActionResult> Post([FromBody] SystemSetup entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        await service.CreateAsync(entity);

        return Created(entity);
    }

    [HttpPut("odata/SystemSetup({key})")]
    [HttpPut("odata/SystemSetup/{key}")]
    public async Task<IActionResult> Put([FromRoute] string key, [FromBody] SystemSetup entity)
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
    
    [HttpDelete("odata/SystemSetup({key})")]
    [HttpDelete("odata/SystemSetup/{key}")]
    public async Task<IActionResult> Delete([FromRoute] string key)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var success = await service.DeleteAsync(key);

            if (!success)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (Exception e)
        {
            if (e is DbUpdateException or DbUpdateConcurrencyException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] string key, [FromBody] Delta<SystemSetup> patch)
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
            if (ex is DefaultException or  ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}
