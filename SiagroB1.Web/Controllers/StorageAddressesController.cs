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
    public ActionResult<IEnumerable<StorageAddress>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<StorageAddress>> GetAsync([FromRoute] string key)
    {
        var item = await getService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    public async Task<IActionResult> PostAsync([FromBody] StorageAddress entity)
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
    
    public async Task<IActionResult> PutAsync(string key, StorageAddress entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await updateService.ExecuteAsync(key, entity, userName);
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

    public async Task<IActionResult> DeleteAsync([FromRoute] string key)
    {
        try
        {
            var success = await deleteService.ExecuteAsync(key);

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
    public virtual async Task<IActionResult> PatchAsync([FromRoute] string key, [FromBody] Delta<StorageAddress> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        StorageAddress? t = await getService.GetByIdAsync(key);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);
            var userName = User.Identity?.Name ?? "Unknown";
            await updateService.ExecuteAsync(key, t, userName);
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