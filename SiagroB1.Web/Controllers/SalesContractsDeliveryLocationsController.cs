using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class SalesContractsDeliveryLocationsController(
    SalesContractsDeliveryLocationsCreateService createService,
    SalesContractsDeliveryLocationsUpdateService updateService,
    SalesContractsDeliveryLocationsDeleteService deleteService,
    SalesContractsDeliveryLocationsGetService getService)
    : ODataController
{
    [HttpPost("odata/SalesContracts({key:guid})/DeliveryLocations")]
    [HttpPost("odata/SalesContracts/{key:guid}/DeliveryLocations")]
    public async Task<ActionResult<SalesContractDeliveryLocation>> PostAsync(
        [FromRoute] Guid key, [FromBody] SalesContractDeliveryLocation associationEntity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            await createService.ExecuteAsync(key, associationEntity);
            return Created(associationEntity);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPut("odata/SalesContracts({parentKey:guid})/DeliveryLocations({associationKey:guid})")]
    [HttpPut("odata/SalesContracts/{parentKey:guid}/DeliveryLocations/{associationKey:guid}")]
    public async Task<IActionResult> PutAsync(
        [FromRoute] Guid parentKey, [FromRoute] Guid associationKey,
        [FromBody] SalesContractDeliveryLocation associationEntity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            await updateService.ExecuteAsync(parentKey, associationKey, associationEntity);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
        return NoContent();
    }

    [HttpDelete("odata/SalesContractsDeliveryLocations({associationKey:guid})")]
    [HttpDelete("odata/SalesContractsDeliveryLocations/{associationKey:guid}")]
    [HttpDelete("odata/SalesContracts({parentKey:guid})/DeliveryLocations({associationKey:guid})")]
    [HttpDelete("odata/SalesContracts/{parentKey:guid}/DeliveryLocations/{associationKey:guid}")]
    public async Task<IActionResult> DeleteAsync([FromRoute] Guid associationKey)
    {
        try
        {
            await deleteService.ExecuteAsync(associationKey);
        }
        catch (NotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
        return NoContent();
    }

    [HttpGet("odata/SalesContracts({key:guid})/DeliveryLocations")]
    [HttpGet("odata/SalesContracts/{key:guid}/DeliveryLocations")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesContractDeliveryLocation>> GetAsync([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }

    [HttpGet("odata/SalesContracts({key:guid})/DeliveryLocations({associationKey:guid})")]
    [HttpGet("odata/SalesContracts/{key:guid}/DeliveryLocations/{associationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<SalesContractDeliveryLocation>> GetAsync(
        [FromRoute] Guid key, [FromRoute] Guid associationKey)
    {
        var item = await getService.GetByIdAsync(key, associationKey);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch(
        [FromRoute] Guid key, [FromBody] Delta<SalesContractDeliveryLocation> patch)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        SalesContractDeliveryLocation? t = await getService.GetByIdAsync(key);
        if (t == null) return NotFound();

        try
        {
            patch.Patch(t);
            await updateService.ExecuteAsync(key, t);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
        return NoContent();
    }
}
