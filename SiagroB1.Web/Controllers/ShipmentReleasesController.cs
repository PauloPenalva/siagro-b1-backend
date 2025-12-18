using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class ShipmentReleasesController(
    ShipmentReleasesCreateService createService,
    ShipmentReleasesDeleteService deleteService,
    ShipmentReleasesGetService getService
    ) : ODataController
{
    [HttpGet("odata/ShipmentReleases")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentRelease>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [HttpGet("odata/ShipmentReleases({key:guid})")]
    [HttpGet("odata/ShipmentReleases/{key:guid}")]
    [EnableQuery]
    public async Task<ActionResult<ShipmentRelease>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }
    
    [HttpPost("odata/ShipmentReleases")]
    public async Task<IActionResult> Post([FromBody] ShipmentRelease entity)
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

    [HttpDelete("odata/ShipmentReleases({key:guid})")]
    [HttpDelete("odata/ShipmentReleases/{key:guid}")]
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

            return StatusCode(500, ex.Message);
        }
    }
}