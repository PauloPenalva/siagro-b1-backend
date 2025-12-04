using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsTaxesController(
    PurchaseContractsTaxesCreateService createService,
    PurchaseContractsTaxesUpdateService updateService,
    PurchaseContractsTaxesDeleteService deleteService,
    PurchaseContractsTaxesGetService getService
    ) 
    : ODataController
{
    [HttpPost("odata/PurchaseContracts({key:guid})/Taxes")]
    [HttpPost("odata/PurchaseContracts/{key:guid}/Taxes")]
    public async Task<ActionResult<PurchaseContractTax>> PostTaxesAsync([FromRoute] Guid key, [FromBody] PurchaseContractTax associationEntity)
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

    [HttpPut("odata/PurchaseContracts({parentKey:guid})/Taxes({associationKey:guid})")]
    [HttpPut("odata/PurchaseContracts/{parentKey:guid}/Taxes/{associationKey:guid}")]
    public async Task<IActionResult> PutTaxesAsync(
        [FromRoute] Guid parentKey, 
        [FromRoute] Guid associationKey,
        [FromBody] PurchaseContractTax associationEntity)
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
    
    [HttpDelete("odata/PurchaseContractsTaxes({associationKey:guid})")]
    public async Task<IActionResult> DeleteTaxesAsync([FromRoute] Guid associationKey)
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

    
    [HttpDelete("odata/PurchaseContracts({parentKey:guid})/Taxes({associationKey:guid})")]
    [HttpDelete("odata/PurchaseContracts/{parentKey:guid}/Taxes/{associationKey:guid}")]
    public async Task<IActionResult> DeleteTaxesAsync([FromRoute] Guid parentKey,[FromRoute] Guid associationKey)
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
    

    [HttpGet("odata/PurchaseContracts({key:guid})/Taxes")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/Taxes")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractPriceFixation>> GetTaxesAsync([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
    
    [HttpGet("odata/PurchaseContracts({key:guid})/Taxes({fixationKey:guid})")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/Taxes/{fixationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractPriceFixation>> GetTaxesAsync([FromRoute] Guid key, [FromRoute] Guid fixationKey)
    {
        var item = await getService.GetByIdAsync(key, fixationKey);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<PurchaseContractTax> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        PurchaseContractTax? t = await getService.GetByIdAsync(key);

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