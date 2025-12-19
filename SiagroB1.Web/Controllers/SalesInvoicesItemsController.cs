using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class SalesInvoicesItemsController(
    SalesInvoicesItemsCreateService createService,
    SalesInvoicesItemsGetService getService,
    SalesInvoicesItemsUpdateService updateService,
    SalesInvoicesItemsDeleteService deleteService
    )
    :ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<SalesInvoiceItem>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<SalesInvoiceItem>> Get([FromRoute] Guid key)
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
    
    public async Task<IActionResult> Post([FromBody] SalesInvoiceItem entity)
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
    
    public async Task<IActionResult> Put([FromRoute] Guid key, [FromBody] SalesInvoiceItem entity)
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
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<SalesInvoiceItem> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        SalesInvoiceItem? t = await getService.GetByIdAsync(key);

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