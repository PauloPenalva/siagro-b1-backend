using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>
/// "Emitir NF-e". Rejeição, denegação e falta de resposta voltam 200 com o desfecho; pré-condição
/// e prontidão voltam 400 com a mensagem.
/// </summary>
public class SalesInvoicesIssueNfeController(SalesInvoicesNfeIssueService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesIssueNfe")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        try
        {
            return Ok(await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown"));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
