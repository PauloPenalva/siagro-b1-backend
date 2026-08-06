using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Documento de entrada: NF de fornecedor, venda futura do produtor e suas remessas, insumo,
/// serviço, e a devolução emitida pelo cliente.
///
/// Confirmar, estornar, cancelar e importar XML passam pelas actions correspondentes.
/// </summary>
public class PurchaseInvoicesController(
    PurchaseInvoicesGetService getService,
    PurchaseInvoicesCreateService createService,
    PurchaseInvoicesUpdateService updateService,
    PurchaseInvoicesDeleteService deleteService,
    PurchaseInvoicesGetOriginItemsService originItemsService)
    : ODataController
{
    // MaxExpansionDepth explícito, como em PurchaseContractsController: o Detail precisa de
    // Items > SalesInvoiceItem > SalesInvoice (profundidade 3) para exibir a NF de origem da
    // devolução, e o padrão do [EnableQuery] é 2. Estourar o limite não degrada nada: devolve 400
    // e a tela inteira nasce EM BRANCO, sem um campo sequer preenchido.
    [EnableQuery(MaxExpansionDepth = 5)]
    public ActionResult<IEnumerable<PurchaseInvoice>> Get() => Ok(getService.QueryAll());

    [EnableQuery(MaxExpansionDepth = 5)]
    public async Task<ActionResult<PurchaseInvoice>> Get([FromRoute] Guid key)
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
    /// Linhas de saída elegíveis como origem, do cliente informado — é o que alimenta o value help
    /// da amarração da devolução.
    /// </summary>
    [HttpGet("odata/PurchaseInvoicesOriginItems(CardCode={cardCode})")]
    [EnableQuery]
    public ActionResult GetOriginItems([FromRoute] string cardCode)
    {
        // Rota por atributo entrega o segmento COM as aspas simples do OData.
        return Ok(originItemsService.QueryByCardCode(cardCode?.Trim('\'') ?? string.Empty));
    }

    public async Task<IActionResult> Post([FromBody] PurchaseInvoice entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");

            return Created(entity);
        }
        catch (Exception e)
        {
            if (e is DefaultException or BusinessException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }

    public async Task<IActionResult> Put([FromRoute] Guid key, [FromBody] PurchaseInvoice entity)
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

    /// <summary>
    /// O <c>GetByIdAsync</c> devolve entidade NÃO rastreada de propósito: se viesse rastreada, o
    /// serviço de atualização carregaria o mesmo objeto como "existente" e a reconciliação das
    /// linhas viraria no-op silencioso.
    /// </summary>
    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch(
        [FromRoute] Guid key, [FromBody] Delta<PurchaseInvoice> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var entity = await getService.GetByIdAsync(key);
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
