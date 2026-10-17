using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseInvoices;

/// <summary>Vínculo do CT-e com as NF-e transportadas: incluir uma chave, remover e refazer a busca (spec 2026-10-17).</summary>
public class PurchaseInvoicesTransportedDocumentsActionsController(PurchaseInvoicesTransportedDocumentsService service)
    : ODataController
{
    [HttpPost("odata/PurchaseInvoicesTransportedDocumentAdd")]
    public async Task<IActionResult> AddAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM; AccessKey é anulável.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        parameters.TryGetValue("AccessKey", out var accessKey);

        return await RunAsync(async () => Ok((await service.AddAsync(key, accessKey?.ToString())).Key));
    }

    [HttpPost("odata/PurchaseInvoicesTransportedDocumentRemove")]
    public async Task<IActionResult> RemoveAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        return await RunAsync(async () =>
        {
            await service.RemoveAsync(key);
            return NoContent();
        });
    }

    [HttpPost("odata/PurchaseInvoicesTransportedDocumentsRelink")]
    public async Task<IActionResult> RelinkAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        return await RunAsync(async () =>
        {
            await service.RelinkAsync(key);
            return NoContent();
        });
    }

    private static async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (NotFoundException e)
        {
            return new NotFoundObjectResult(e.Message);
        }
        catch (DefaultException e)
        {
            return new BadRequestObjectResult(e.Message);
        }
    }
}
