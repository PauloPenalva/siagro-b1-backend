using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PaymentConditions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Condições de pagamento — cadastro só do modo STANDALONE. O item de menu é escondido fora dele
/// (MENU_ITEMS.StandaloneOnly); recusar aqui é a defesa de quem chega pela API.
/// </summary>
public class PaymentConditionsController(PaymentConditionsService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "As condições de pagamento só existem no modo STANDALONE.";

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

        var condition = await service.GetByIdAsync(key);

        return condition is null ? NotFound() : Ok(condition);
    }

    public async Task<IActionResult> Post([FromBody] PaymentCondition entity)
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
    public async Task<IActionResult> Patch([FromODataUri] int key, [FromBody] Delta<PaymentCondition> patch)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        if (!ModelState.IsValid) return BadRequest(ModelState);

        var condition = await service.GetByIdAsync(key);

        if (condition is null) return NotFound();

        try
        {
            patch.Patch(condition);
            await service.UpdateAsync(condition);
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromODataUri] int key)
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
