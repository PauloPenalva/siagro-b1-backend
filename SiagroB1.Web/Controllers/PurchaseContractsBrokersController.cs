using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsBrokersController(
    PurchaseContractsBrokersCreateService createService,
    PurchaseContractsBrokersUpdateService updateService,
    PurchaseContractsBrokersDeleteService deleteService,
    PurchaseContractsBrokersGetService getService
    ) 
    : ODataController
{
    [HttpPost("odata/PurchaseContracts({key:guid})/Brokers")]
    [HttpPost("odata/PurchaseContracts/{key:guid}/Brokers")]
    public async Task<ActionResult<PurchaseContractBroker>> PostAsync(
        [FromRoute] Guid key, 
        [FromBody] PurchaseContractBroker associationEntity)
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

    [HttpPut("odata/PurchaseContracts({parentKey:guid})/Brokers({associationKey:guid})")]
    [HttpPut("odata/PurchaseContracts/{parentKey:guid}/Brokers/{associationKey:guid}")]
    public async Task<IActionResult> PutAsync(
        [FromRoute] Guid parentKey, 
        [FromRoute] Guid associationKey,
        [FromBody] PurchaseContractBroker associationEntity)
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
    
    [HttpDelete("odata/PurchaseContractsBrokers({associationKey:guid})")]
    [HttpDelete("odata/PurchaseContractsBrokers/{associationKey:guid}")]
    [HttpDelete("odata/PurchaseContracts({parentKey:guid})/Brokers({associationKey:guid})")]
    [HttpDelete("odata/PurchaseContracts/{parentKey:guid}/Brokers/{associationKey:guid}")]
    public async Task<IActionResult> DeletesAsync([FromRoute] Guid associationKey)
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
    

    [HttpGet("odata/PurchaseContracts({key:guid})/Brokers")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/Brokers")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractBroker>> GetAsync([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
    
    [HttpGet("odata/PurchaseContracts({key:guid})/Brokers({fixationKey:guid})")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/Brokers/{fixationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractBroker>> GetAsync([FromRoute] Guid key, [FromRoute] Guid fixationKey)
    {
        var item = await getService.GetByIdAsync(key, fixationKey);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<PurchaseContractBroker> patch)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        PurchaseContractBroker? t = await getService.GetByIdAsync(key);

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