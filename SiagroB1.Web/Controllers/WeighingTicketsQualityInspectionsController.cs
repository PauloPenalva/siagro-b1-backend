using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.WeighingTickets;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WeighingTicketsQualityInspectionsController(
    WeighingTicketsQualityInspectionsCreateService createService,
    WeighingTicketsQualityInspectionsUpdateService updateService,
    WeighingTicketsQualityInspectionsDeleteService deleteService,
    WeighingTicketsQualityInspectionsGetService getService
    ) 
    : ODataController
{
    [HttpPost("odata/WeighingTickets({key:guid})/QualityInspections")]
    [HttpPost("odata/WeighingTickets/{key:guid}/QualityInspections")]
    public async Task<ActionResult<QualityInspection>> PostQualityParametersAsync([FromRoute] Guid key, [FromBody] QualityInspection associationEntity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            await createService.ExecuteAsync(key, associationEntity);

            return Created(associationEntity);
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

    [HttpPut("odata/WeighingTickets({parentKey:guid})/QualityInspections({associationKey:guid})")]
    [HttpPut("odata/WeighingTickets/{parentKey:guid}/QualityInspections/{associationKey:guid}")]
    public async Task<IActionResult> PutQualityParametersAsync(
        [FromRoute] Guid parentKey, 
        [FromRoute] Guid associationKey,
        [FromBody] QualityInspection associationEntity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await updateService.ExecuteAsync(parentKey, associationKey, associationEntity);
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
    
    
    [HttpDelete("odata/WeighingTickets({parentKey:guid})/QualityInspections({associationKey:guid})")]
    [HttpDelete("odata/WeighingTickets/{parentKey:guid}/QualityInspections/{associationKey:guid}")]
    public async Task<IActionResult> DeleteQualityParametersAsync([FromRoute] Guid parentKey,[FromRoute] Guid associationKey)
    {
        try
        {
            await deleteService.ExecuteAsync(parentKey, associationKey);
        }
        catch (NotFoundException)
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

    [HttpDelete("odata/WeighingTicketsQualityInspections({associationKey:guid})")]
    public async Task<IActionResult> DeleteQualityParametersAsync([FromRoute] Guid associationKey)
    {
        try
        {
            await deleteService.ExecuteAsync(associationKey);
        }
        catch (NotFoundException)
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

    [HttpGet("odata/WeighingTickets({key:guid})/QualityInspections")]
    [HttpGet("odata/WeighingTickets/{key:guid}/QualityInspections")]
    [EnableQuery]
    public ActionResult<IEnumerable<QualityInspection>> GetQualityParameters([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
    
    [HttpGet("odata/WeighingTickets({key:guid})/QualityInspections({associationKey:guid})")]
    [HttpGet("odata/WeighingTickets/{key:guid}/QualityInspections/{associationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<QualityInspection>> GetQualityParametersAsync([FromRoute] Guid key, [FromRoute] Guid associationKey)
    {
        try
        {
            var item = await getService.GetByIdAsync(key, associationKey);
            return Ok(item);
        }
        catch (Exception e)
        {
            if (e is NotFoundException)
                return NotFound(e.Message);
            
            return StatusCode(500, e.Message);
        }
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<QualityInspection> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }     

        QualityInspection? t = await getService.GetByIdAsync(key);

        if (t == null)
        {
            return NotFound();
        }

        try
        {
            patch.Patch(t);

            await updateService.ExecuteAsync(key, t);
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