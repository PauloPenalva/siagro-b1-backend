using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.FiscalInventory;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Saldo inicial do estoque fiscal (Kardex Fiscal) — cadastro só do modo STANDALONE. Recusar aqui também é a
/// defesa de quem chega pela API sem passar pelo menu (que some em SAPB1).
/// </summary>
public class FiscalInventoryOpeningBalancesController(
    IFiscalInventoryOpeningBalancesService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

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

        var balance = await service.GetByKeyAsync(key);

        return balance is null ? NotFound() : Ok(balance);
    }

    public async Task<IActionResult> Post([FromBody] FiscalInventoryOpeningBalance entity)
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
    public async Task<IActionResult> Patch([FromODataUri] Guid key, [FromBody] Delta<FiscalInventoryOpeningBalance> patch)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        if (!ModelState.IsValid) return BadRequest(ModelState);

        var balance = await service.GetByKeyAsync(key);

        if (balance is null) return NotFound();

        try
        {
            patch.Patch(balance);
            await service.UpdateAsync(key, balance);
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key)
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
