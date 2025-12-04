using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsQualityParametersController(
    PurchaseContractsQualityParametersCreateService createService,
    PurchaseContractsQualityParametersUpdateService updateService,
    PurchaseContractsQualityParametersDeleteService deleteService,
    PurchaseContractsQualityParametersGetService getService
    ) 
    : ODataController
{
    [HttpPost("odata/PurchaseContracts({key:guid})/QualityParameters")]
    [HttpPost("odata/PurchaseContracts/{key:guid}/QualityParameters")]
    public async Task<ActionResult<PurchaseContractQualityParameter>> PostQualityParametersAsync([FromRoute] Guid key, [FromBody] PurchaseContractQualityParameter associationEntity)
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
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }

    [HttpPut("odata/PurchaseContracts({parentKey:guid})/QualityParameters({associationKey:guid})")]
    [HttpPut("odata/PurchaseContracts/{parentKey:guid}/QualityParameters/{associationKey:guid}")]
    public async Task<IActionResult> PutQualityParametersAsync(
        [FromRoute] Guid parentKey, 
        [FromRoute] Guid associationKey,
        [FromBody] PurchaseContractQualityParameter associationEntity)
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
    
    
    [HttpDelete("odata/PurchaseContracts({parentKey:guid})/QualityParameters({associationKey:guid})")]
    [HttpDelete("odata/PurchaseContracts/{parentKey:guid}/QualityParameters/{associationKey:guid}")]
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

    [HttpDelete("odata/PurchaseContractsQualityParameters({associationKey:guid})")]
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

    [HttpGet("odata/PurchaseContracts({key:guid})/QualityParameters")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/QualityParameters")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractQualityParameter>> GetQualityParameters([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
    
    [HttpGet("odata/PurchaseContracts({key:guid})/QualityParameters({fixationKey:guid})")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/QualityParameters/{fixationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractQualityParameter>> GetQualityParametersAsync([FromRoute] Guid key, [FromRoute] Guid fixationKey)
    {
        var item = await getService.GetByIdAsync(key, fixationKey);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<PurchaseContractQualityParameter> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        PurchaseContractQualityParameter? t = await getService.GetByIdAsync(key);

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
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }

}