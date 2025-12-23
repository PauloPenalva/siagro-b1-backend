using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class StorageAddressesController(
    StorageAddressesCreateService createService,
    StorageAddressesDeleteService deleteService,
    StorageAddressesGetService getService,
    StorageAddressesUpdateService updateService
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/StorageAddresses")]
    public ActionResult<IEnumerable<StorageAddress>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    [HttpGet("odata/StorageAddresses/{code}")]
    [HttpGet("odata/StorageAddresses({code})")]
    public async Task<ActionResult<StorageAddress>> Get([FromRoute] string code)
    {
        var item = await getService.GetByIdAsync(code);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpPost("odata/StorageAddresses")]
    public async Task<IActionResult> Post([FromBody] StorageAddress entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await createService.ExecuteAsync(entity, userName);

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
    
    [HttpPut("odata/StorageAddresses/{code}")]
    [HttpPut("odata/StorageAddresses({code})")]
    public async Task<IActionResult> Put(string code, StorageAddress entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await updateService.ExecuteAsync(code, entity, userName);
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

    [HttpDelete("odata/StorageAddresses/{code}")]
    [HttpDelete("odata/StorageAddresses({code})")]
    public async Task<IActionResult> Delete([FromRoute] string code)
    {
        try
        {
            var success = await deleteService.ExecuteAsync(code);

            if (!success)
            {
                return NotFound();
            }

            return NoContent();

        }
        catch (Exception ex)
        {
            if (ex is DefaultException or ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            if (ex is NotFoundException)
            {
                return NotFound(ex.Message);
            }
            
            return StatusCode(500, ex.Message);
        }
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] string code, [FromBody] Delta<StorageAddress> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        StorageAddress? t = await getService.GetByIdAsync(code);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);
            var userName = User.Identity?.Name ?? "Unknown";
            await updateService.ExecuteAsync(code, t, userName);
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