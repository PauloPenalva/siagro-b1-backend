using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;
using SiagroB1.Web.Filters;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Cadastro de instrumentos. Os códigos de exemplo têm espaço ("DOL COM"): dentro do $batch a chave chega
/// codificada e o GET/PATCH/DELETE dava 404, por isso toda action com chave normaliza antes. Regra de negócio
/// (BusinessException) vira 400 com a mensagem — a base a transformaria em 500.
/// </summary>
[MarketModuleEnabled]
public class MarketInstrumentsController(MarketInstrumentService service)
    : ODataBaseController<MarketInstrument, string>(service)
{
    [EnableQuery]
    public override Task<ActionResult<MarketInstrument>> Get([FromRoute] string key) =>
        base.Get(ODataKeyNormalizer.Normalize(key));

    public override async Task<IActionResult> Post([FromBody] MarketInstrument entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        return await Guard(async () =>
        {
            await service.CreateAsync(entity);
            return Created(entity);
        });
    }

    public override async Task<IActionResult> Put([FromRoute] string key, [FromBody] MarketInstrument entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        return await Guard(async () =>
        {
            await service.UpdateAsync(ODataKeyNormalizer.Normalize(key), entity);
            return NoContent();
        });
    }

    public override Task<IActionResult> Delete([FromRoute] string key) =>
        Guard(async () => await service.DeleteAsync(ODataKeyNormalizer.Normalize(key)) ? NoContent() : NotFound());

    [AcceptVerbs("PATCH", "MERGE")]
    public override async Task<IActionResult> Patch([FromODataUri] string key, [FromBody] Delta<MarketInstrument> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var code = ODataKeyNormalizer.Normalize(key);
        var instrument = await service.GetByIdAsync(code);
        if (instrument is null)
            return NotFound();

        return await Guard(async () =>
        {
            patch.Patch(instrument);
            await service.UpdateAsync(code, instrument);
            return NoContent();
        });
    }

    // Mesmo mapeamento da base (KeyNotFound → 404, DefaultException → 400, resto → 500) mais BusinessException → 400.
    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception e) when (e is BusinessException or DefaultException)
        {
            return BadRequest(e.Message);
        }
        catch (Exception e)
        {
            return StatusCode(500, e.Message);
        }
    }
}
