using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Alíquotas de IBS/CBS por vigência — cadastro só do modo STANDALONE (em SAPB1 a tributação é
/// do SAP). O botão que abre a tela some em SAPB1; recusar aqui também é a defesa de quem chega
/// pela API.
/// </summary>
public class IbsCbsRatesController(IbsCbsRatesService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "As alíquotas de IBS/CBS só existem no modo STANDALONE.";

    private bool IsStandalone => ErpMode.IsStandalone(configuration);

    [EnableQuery]
    public ActionResult Get()
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult> Get([FromRoute] int key)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        var rate = await service.GetByIdAsync(key);

        return rate is null ? NotFound() : Ok(rate);
    }

    public async Task<IActionResult> Post([FromBody] IbsCbsRate entity)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            await service.CreateAsync(entity);

            return Created(entity);
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromODataUri] int key, [FromBody] Delta<IbsCbsRate> patch)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        if (!ModelState.IsValid) return BadRequest(ModelState);

        var rate = await service.GetByIdAsync(key);

        if (rate is null) return NotFound();

        try
        {
            patch.Patch(rate);
            await service.UpdateAsync(key, rate);
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromRoute] int key)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        try
        {
            return await service.DeleteAsync(key) ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }
    }
}
