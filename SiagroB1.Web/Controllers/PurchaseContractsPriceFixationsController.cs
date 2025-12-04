using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsPriceFixationsController(
    PurchaseContractsPriceFixationsCreateService createService,
    PurchaseContractsPriceFixationsUpdateService updateService,
    PurchaseContractsPriceFixationsDeleteService deleteService,
    PurchaseContractsPriceFixationsGetService getService
    ) 
    : ODataController
{
    [HttpPost("odata/PurchaseContracts({key:guid})/PriceFixations")]
    [HttpPost("odata/PurchaseContracts/{key:guid}/PriceFixations")]
    public async Task<ActionResult<PurchaseContractPriceFixation>> CreatePriceFixationsAsync([FromRoute] Guid key, [FromBody] PurchaseContractPriceFixation associationEntity)
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

    [HttpPut("odata/PurchaseContracts({parentKey:guid})/PriceFixations({associationKey:guid})")]
    [HttpPut("odata/PurchaseContracts/{parentKey:guid}/PriceFixations/{associationKey:guid}")]
    public async Task<IActionResult> UpdatePriceFixationsAsync(
        [FromRoute] Guid parentKey, 
        [FromRoute] Guid associationKey,
        [FromBody] PurchaseContractPriceFixation associationEntity)
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
    
    [HttpDelete("odata/PurchaseContractsPriceFixations({associationKey:guid})")]
    [HttpDelete("odata/PurchaseContractsPriceFixations/{associationKey:guid}")]
    public async Task<IActionResult> DeletePriceFixationsAsync([FromRoute] Guid associationKey)
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

    
    [HttpDelete("odata/PurchaseContracts({parentKey:guid})/PriceFixations({associationKey:guid})")]
    [HttpDelete("odata/PurchaseContracts/{parentKey:guid}/PriceFixations/{associationKey:guid}")]
    public async Task<IActionResult> DeletePriceFixationsAsync([FromRoute] Guid parentKey,[FromRoute] Guid associationKey)
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
    

    [HttpGet("odata/PurchaseContracts({key:guid})/PriceFixations")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/PriceFixations")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractPriceFixation>> GetPriceFixations([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
    
    [HttpGet("odata/PurchaseContracts({key:guid})/PriceFixations({fixationKey:guid})")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/PriceFixations/{fixationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractPriceFixation>> Get([FromRoute] Guid key, [FromRoute] Guid fixationKey)
    {
        var item = await getService.GetByIdAsync(key, fixationKey);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<PurchaseContractPriceFixation> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        PurchaseContractPriceFixation? t = await getService.GetByIdAsync(key);

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