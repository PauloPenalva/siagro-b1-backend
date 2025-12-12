using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class StorageTransactionsQualityInspectionsController(
    StorageTransactionsQualityInspectionsCreateService createService,
    StorageTransactionsQualityInspectionsUpdateService updateService,
    StorageTransactionsQualityInspectionsDeleteService deleteService,
    StorageTransactionsQualityInspectionsGetService getService
    ) 
    : ODataController
{
    [HttpPost("odata/StorageTransactions({key:guid})/QualityInspections")]
    [HttpPost("odata/StorageTransactions/{key:guid}/QualityInspections")]
    public async Task<ActionResult<StorageTransactionQualityInspection>> PostQualityParametersAsync([FromRoute] Guid key, [FromBody] StorageTransactionQualityInspection associationEntity)
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

    [HttpPut("odata/StorageTransactions({parentKey:guid})/QualityInspections({associationKey:guid})")]
    [HttpPut("odata/StorageTransactions/{parentKey:guid}/QualityInspections/{associationKey:guid}")]
    public async Task<IActionResult> PutQualityParametersAsync(
        [FromRoute] Guid parentKey, 
        [FromRoute] Guid associationKey,
        [FromBody] StorageTransactionQualityInspection associationEntity)
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
    
    
    [HttpDelete("odata/StorageTransactions({parentKey:guid})/QualityInspections({associationKey:guid})")]
    [HttpDelete("odata/StorageTransactions/{parentKey:guid}/QualityInspections/{associationKey:guid}")]
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

    [HttpDelete("odata/StorageTransactionsQualityInspections({associationKey:guid})")]
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

    [HttpGet("odata/StorageTransactions({key:guid})/QualityInspections")]
    [HttpGet("odata/StorageTransactions/{key:guid}/QualityInspections")]
    [EnableQuery]
    public ActionResult<IEnumerable<StorageTransactionQualityInspection>> GetQualityParameters([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
    
    [HttpGet("odata/StorageTransactions({key:guid})/QualityInspections({fixationKey:guid})")]
    [HttpGet("odata/StorageTransactions/{key:guid}/QualityInspections/{fixationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<StorageTransactionQualityInspection>> GetQualityParametersAsync([FromRoute] Guid key, [FromRoute] Guid fixationKey)
    {
        var item = await getService.GetByIdAsync(key, fixationKey);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<StorageTransactionQualityInspection> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        StorageTransactionQualityInspection? t = await getService.GetByIdAsync(key);

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