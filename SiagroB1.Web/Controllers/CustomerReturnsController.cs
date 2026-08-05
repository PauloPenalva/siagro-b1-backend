using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.CustomerReturns;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class CustomerReturnsController(
    CustomerReturnsGetService getService,
    CustomerReturnsCreateService createService,
    CustomerReturnsUpdateService updateService,
    CustomerReturnsGetOriginItemsService originItemsService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<CustomerReturn>> Get()
    {
        return Ok(getService.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult<CustomerReturn>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await getService.GetByIdAsync(key));
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Linhas de saída elegíveis como origem, do cliente informado — é o que alimenta o value
    /// help da amarração.
    /// </summary>
    [HttpGet("odata/CustomerReturnsOriginItems(CardCode={cardCode})")]
    [EnableQuery]
    public ActionResult GetOriginItems([FromRoute] string cardCode)
    {
        // Rota por atributo entrega o segmento COM as aspas simples do OData.
        return Ok(originItemsService.QueryByCardCode(cardCode?.Trim('\'') ?? string.Empty));
    }

    public async Task<IActionResult> Post([FromBody] CustomerReturn entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await createService.ExecuteAsync(entity, userName);

            return Created(entity);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
                return BadRequest(ex.Message);

            return StatusCode(500, ex.Message);
        }
    }

    public async Task<IActionResult> Put([FromRoute] Guid key, [FromBody] CustomerReturn entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await updateService.ExecuteAsync(key, entity, userName);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
                return BadRequest(ex.Message);

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<CustomerReturn> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var entity = await getService.GetByIdAsync(key);
            patch.Patch(entity);

            var userName = User.Identity?.Name ?? "Unknown";
            await updateService.ExecuteAsync(key, entity, userName);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
                return BadRequest(ex.Message);

            return StatusCode(500, ex.Message);
        }

        return NoContent();
    }
}
