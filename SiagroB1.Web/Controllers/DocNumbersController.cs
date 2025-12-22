using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class DocNumbersController(DocNumberService service) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/DocNumbers")]
    public ActionResult<IEnumerable<DocType>> Get()
    {
        return Ok(service.QueryAll());
    }
    
    [EnableQuery]
    [HttpGet("odata/DocNumbers({key})")]
    [HttpGet("odata/DocNumbers/{key}")]
    public async Task<ActionResult<DocType>> Get([FromRoute] Guid key)
    {
        var item = await service.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [HttpPost("odata/DocNumbers")]
    public async Task<IActionResult> Post([FromBody] DocNumber entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        await service.CreateAsync(entity);

        return Created(entity);
    }

    [HttpPut("odata/DocNumbers({key})")]
    [HttpPut("odata/DocNumbers/{key}")]
    public async Task<IActionResult> Put([FromRoute] Guid key, [FromBody] DocNumber entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (key != entity.Key)
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
    
    [HttpDelete("odata/DocNumbers({key})")]
    [HttpDelete("odata/DocNumbers/{key}")]
    public async Task<IActionResult> Delete([FromRoute] Guid key)
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
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<DocNumber> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        DocNumber? t = await service.GetByIdAsync(key);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);
            var userName = User.Identity?.Name ?? "Unknown";
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
