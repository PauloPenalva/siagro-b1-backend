using Microsoft.AspNetCore.Mvc;
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
    [HttpPost("odata/PurchaseContracts({key})/PriceFixations")]
    [HttpPost("odata/PurchaseContracts/{key}/PriceFixations")]
    public async Task<ActionResult<PurchaseContractPriceFixation>> Post([FromRoute] Guid key, [FromBody] PurchaseContractPriceFixation associationEntity)
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

    [HttpPut("odata/PurchaseContracts({parentKey})/PriceFixations({associationKey})")]
    [HttpPut("odata/PurchaseContracts/{parentKey}/PriceFixations/{associationKey}")]
    public async Task<IActionResult> Put(
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
    
    
    [HttpDelete("odata/PurchaseContracts({parentKey})/PriceFixations({associationKey})")]
    [HttpDelete("odata/PurchaseContracts/{parentKey}/PriceFixations/{associationKey}")]
    public async Task<IActionResult> Delete([FromRoute] Guid parentKey,[FromRoute] Guid associationKey)
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
    

    [HttpGet("odata/PurchaseContracts({key})/PriceFixations")]
    [HttpGet("odata/PurchaseContracts/{key}/PriceFixations")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractPriceFixation>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
    
    [HttpGet("odata/PurchaseContracts({key})/PriceFixations({fixationKey})")]
    [HttpGet("odata/PurchaseContracts/{key}/PriceFixations/{fixationKey}")]
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
}