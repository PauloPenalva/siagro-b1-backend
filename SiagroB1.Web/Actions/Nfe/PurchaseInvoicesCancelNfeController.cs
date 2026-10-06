using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class PurchaseInvoicesCancelNfeController(PurchaseInvoicesNfeCancelService service) : ODataController
{
    [HttpPost("odata/PurchaseInvoicesCancelNfe")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        // Parâmetro string ausente/nulo: não chamar ToString() sobre nulo.
        parameters.TryGetValue("Justification", out var justificationObj);

        try
        {
            return Ok(await service.ExecuteAsync(key, justificationObj as string, User.Identity?.Name ?? "Unknown"));
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
