using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class SalesShipmentReleasesController(
    SalesShipmentReleasesCreateService createService,
    SalesShipmentReleasesDeleteService deleteService,
    SalesShipmentReleasesGetService getService
    ) : ODataController
{
    [HttpGet("odata/SalesShipmentReleases")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesShipmentRelease>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [HttpGet("odata/SalesShipmentReleases({key:guid})")]
    [HttpGet("odata/SalesShipmentReleases/{key:guid}")]
    [EnableQuery]
    public async Task<ActionResult<SalesShipmentRelease>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [HttpPost("odata/SalesShipmentReleases")]
    public async Task<IActionResult> Post([FromBody] SalesShipmentRelease entity)
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
            // As guardas de negócio do create (contrato encerrado, saldo físico, local de
            // entrega fora do contrato) lançam ApplicationException: são recusas de regra,
            // não falhas do servidor. Mesmo mapeamento já usado no Delete abaixo.
            if (ex is DefaultException or ApplicationException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }

    [HttpDelete("odata/SalesShipmentReleases({key:guid})")]
    [HttpDelete("odata/SalesShipmentReleases/{key:guid}")]
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
