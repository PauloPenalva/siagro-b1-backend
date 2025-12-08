using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class StorageTransactionsController(
    StorageTransactionsCreateService createService,
    StorageTransactionsDeleteService deleteService,
    StorageTransactionsGetService getService,
    StorageTransactionsUpdateService updateService
    ) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<StorageTransaction>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<StorageTransaction>> Get([FromRoute] Guid key)
    {
        try
        {
            var item = await getService.GetByIdAsync(key);
            return Ok(item);
        }
        catch (Exception ex)
        {
            if (ex is NotFoundException)
            {
                return NotFound();
            }
            
            return BadRequest(ex.Message);
        }
    }
    
    public async Task<IActionResult> Post([FromBody] StorageTransaction entity)
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
    
    public async Task<IActionResult> Put(Guid key, StorageTransaction entity)
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

    public async Task<IActionResult> Delete([FromRoute] Guid key)
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
}