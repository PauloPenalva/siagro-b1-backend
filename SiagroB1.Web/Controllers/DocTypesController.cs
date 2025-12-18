using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class DocTypesController(DocTypeService service) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<DocType>> Get()
    {
        return Ok(service.QueryAll());
    }
    
    [EnableQuery]
    public async Task<ActionResult<DocType>> Get([FromRoute] string code)
    {
        var item = await service.GetByIdAsync(code);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] DocType entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        await service.CreateAsync(entity);

        return Created(entity);
    }

    public async Task<IActionResult> Put([FromRoute] string code, [FromBody] DocType entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (code != entity.Code)
        {
            return BadRequest();
        }

        try
        {
            await service.UpdateAsync(code, entity);
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
    
    public async Task<IActionResult> Delete([FromRoute] string code)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var success = await service.DeleteAsync(code);

        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] string code, [FromBody] Delta<DocType> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        DocType? t = await service.GetByIdAsync(code);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);
            var userName = User.Identity?.Name ?? "Unknown";
            await service.UpdateAsync(code, t);
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
