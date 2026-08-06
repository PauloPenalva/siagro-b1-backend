using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseInvoices;

public class PurchaseInvoicesCommentCreateController(
    PurchaseInvoicesCommentCreateService service) : ODataController
{
    [HttpPost("odata/PurchaseInvoicesCommentCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("InvoiceKey", out var keyObj))
                return BadRequest("Chave do documento não informada.");

            parameters.TryGetValue("Text", out var textObj);

            await service.ExecuteAsync(
                (Guid) keyObj, textObj as string, User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
