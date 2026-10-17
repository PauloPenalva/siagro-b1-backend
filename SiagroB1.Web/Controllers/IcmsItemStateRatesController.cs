using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Alíquota de ICMS por produto e UF — cadastro só do modo STANDALONE. Mesma defesa de quem chega pela API
/// (spec 2026-10-17 §2).
/// </summary>
public class IcmsItemStateRatesController(IcmsItemStateRatesService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "A alíquota de ICMS por produto só existe no modo STANDALONE.";

    private bool IsStandalone => ErpMode.IsStandalone(configuration);

    [EnableQuery]
    public ActionResult Get()
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult> Get([FromRoute] Guid key)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        var setting = await service.GetByIdAsync(key);

        return setting is null ? NotFound() : Ok(setting);
    }

    public async Task<IActionResult> Post([FromBody] IcmsItemStateRate entity)
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
    public async Task<IActionResult> Patch([FromODataUri] Guid key, [FromBody] Delta<IcmsItemStateRate> patch)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        if (!ModelState.IsValid) return BadRequest(ModelState);

        var setting = await service.GetByIdAsync(key);

        if (setting is null) return NotFound();

        try
        {
            // A chave técnica: trocá-la num PATCH quebraria a entidade rastreada.
            if (patch.GetChangedPropertyNames().Contains(nameof(IcmsItemStateRate.Key)))
                return BadRequest("A chave não pode ser alterada.");

            patch.Patch(setting);
            await service.UpdateAsync(setting);
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromODataUri] Guid key)
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
