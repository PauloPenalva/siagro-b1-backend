using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Linhas do documento de entrada — é por aqui que a grade grava a AMARRAÇÃO com a nota de origem.
///
/// A guarda "só documento pendente" também vive nos serviços de linha: este caminho não passa pelo
/// serviço de atualização do cabeçalho.
/// </summary>
public class PurchaseInvoicesItemsController(
    PurchaseInvoicesItemsGetService getService,
    PurchaseInvoicesItemsCreateService createService,
    PurchaseInvoicesItemsUpdateService updateService,
    PurchaseInvoicesItemsDeleteService deleteService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseInvoiceItem>> Get() => Ok(getService.QueryAll());

    /// <summary>
    /// Navegação <c>PurchaseInvoices({key})/Items</c> — o mesmo par de rotas que
    /// <see cref="PurchaseInvoicesCommentsController"/> e
    /// <see cref="PurchaseInvoicesChangeLogsController"/> declaram para as outras coleções filhas.
    ///
    /// Método SEPARADO de propósito: o <c>Get(Guid key)</c> abaixo é a rota por convenção de
    /// <c>PurchaseInvoicesItems({key})</c>, onde a chave é a da LINHA — aqui é a do documento.
    /// </summary>
    [HttpGet("odata/PurchaseInvoices({key:guid})/Items")]
    [HttpGet("odata/PurchaseInvoices/{key:guid}/Items")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseInvoiceItem>> GetByInvoice([FromRoute] Guid key) =>
        Ok(getService.QueryAll(key));

    [EnableQuery]
    public async Task<ActionResult<PurchaseInvoiceItem>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        return item is null ? NotFound() : Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] PurchaseInvoiceItem entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");

            return Created(entity);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception e)
        {
            if (e is DefaultException or BusinessException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }

    public async Task<IActionResult> Put([FromRoute] Guid key, [FromBody] PurchaseInvoiceItem entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await updateService.ExecuteAsync(key, entity, User.Identity?.Name ?? "Unknown");
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception e)
        {
            if (e is DefaultException or BusinessException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }

        return NoContent();
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch(
        [FromRoute] Guid key, [FromBody] Delta<PurchaseInvoiceItem> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var entity = await getService.GetByIdAsync(key);

            if (entity is null)
                return NotFound();

            patch.Patch(entity);

            await updateService.ExecuteAsync(key, entity, User.Identity?.Name ?? "Unknown");
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception e)
        {
            if (e is DefaultException or BusinessException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception e)
        {
            if (e is DefaultException or BusinessException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }

        return NoContent();
    }
}
