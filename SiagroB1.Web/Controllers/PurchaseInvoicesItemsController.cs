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

    /// <summary>
    /// Leitura de UMA linha pelo caminho do documento, <c>PurchaseInvoices({key})/Items({itemKey})</c>.
    ///
    /// Não é rota de tela: é o modelo relendo a linha logo depois do POST (201), com o $select/$expand
    /// da grade, para trocar o registro provisório pelo que o servidor gravou. Sem ela a linha entra
    /// no banco e mesmo assim a tela mostra "Communication error: 404". Só o GET precisa existir: o
    /// UI5 apaga e altera a linha pelo caminho canônico <c>PurchaseInvoicesItems({itemKey})</c>, que o
    /// roteamento por convenção já cobre (<see cref="Delete"/>, <see cref="Patch"/>). Mesmo padrão de
    /// <c>NotificationGroupMembersController.GetFromMembers</c>.
    /// </summary>
    [HttpGet("odata/PurchaseInvoices({key:guid})/Items({itemKey:guid})")]
    [HttpGet("odata/PurchaseInvoices/{key:guid}/Items/{itemKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseInvoiceItem>> GetFromInvoice(
        [FromRoute] Guid key, [FromRoute] Guid itemKey)
    {
        var item = await getService.GetByIdAsync(itemKey);

        return item is null || item.PurchaseInvoiceKey != key ? NotFound() : Ok(item);
    }

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

        return await CreateAsync(entity);
    }

    /// <summary>
    /// Inclusão de linha a partir do documento já gravado.
    ///
    /// A grade da tela de edição é ligada em <c>Items</c>, relativa ao contexto do cabeçalho, então
    /// o <c>create()</c> do modelo posta em <c>PurchaseInvoices({key})/Items</c> — e não na coleção
    /// <c>PurchaseInvoicesItems</c>, que é a única que o roteamento por convenção cobre. Como o GET
    /// desse caminho já era declarado à mão (<see cref="GetByInvoice"/>), a URL existia só para GET e
    /// o POST batia em "405 Method Not Allowed". Mesmo padrão de
    /// <c>NotificationGroupMembersController.PostToMembers</c>.
    /// </summary>
    [HttpPost("odata/PurchaseInvoices({key:guid})/Items")]
    [HttpPost("odata/PurchaseInvoices/{key:guid}/Items")]
    public async Task<IActionResult> PostToInvoice([FromRoute] Guid key, [FromBody] PurchaseInvoiceItem entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Quem diz a que documento a linha pertence é a rota; vale mais que qualquer valor do corpo.
        entity.PurchaseInvoiceKey = key;

        return await CreateAsync(entity);
    }

    private async Task<IActionResult> CreateAsync(PurchaseInvoiceItem entity)
    {
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
