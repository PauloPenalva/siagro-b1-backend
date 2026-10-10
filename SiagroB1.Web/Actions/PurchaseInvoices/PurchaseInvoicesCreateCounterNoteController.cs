using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseInvoices;

/// <summary>"Gerar contra-nota": cria a contra-nota Pendente a partir da escrituração e devolve a chave dela (spec 2026-10-10 §5).</summary>
public class PurchaseInvoicesCreateCounterNoteController(PurchaseInvoicesCounterNoteCreateService service) : ODataController
{
    [HttpPost("odata/PurchaseInvoicesCreateCounterNote")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        try
        {
            var created = await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown");
            return Ok(created.Key);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }
}
