using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Model;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WarehousesController(IWarehouseService service) 
    : ODataController
{
   
    [EnableQuery]
    public virtual ActionResult Get()
    {
        return Ok(service.QueryAll());
    }
    
    
    [EnableQuery]
    public virtual async Task<ActionResult> Get([FromRoute] string key)
    {
        var item = await service.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
       
    public virtual async Task<IActionResult> Post([FromBody] WarehouseModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        try
        {
            await service.CreateAsync(model);

            return Created(model);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException or NotImplementedException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

    }
    
    public virtual async Task<IActionResult> Put([FromRoute] string key, [FromBody] WarehouseModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await service.UpdateAsync(key, model);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException or NotImplementedException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
    
    public virtual async Task<IActionResult> Delete([FromRoute] string key)
    {
        try
        {
            bool success = await service.DeleteAsync(key);

            if (!success)
            {
                return NotFound();
            }

            return NoContent();

        }
        catch (Exception ex)
        {
            if (ex is DefaultException or NotImplementedException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromODataUri] string key, [FromBody] Delta<WarehouseModel> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        WarehouseModel? t = await service.GetByIdAsync(key);

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
            if (ex is DefaultException or NotImplementedException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}
