using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Actions.PurchaseInvoices;

public class PurchaseInvoicesCommentUpdateController(
    PurchaseInvoicesCommentUpdateService service) : ODataController
{
    /// <summary>
    /// <c>Key</c> é a chave do COMENTÁRIO. Só o autor altera o próprio comentário; admin altera
    /// qualquer um — a permissão é decidida no servidor, a tela só evita a viagem inútil.
    /// </summary>
    [HttpPost("odata/PurchaseInvoicesCommentUpdate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Chave do comentário não informada.");

            parameters.TryGetValue("Text", out var textObj);

            await service.ExecuteAsync(
                (Guid) keyObj, textObj as string, User.Identity?.Name ?? "Unknown", User.IsAdmin());

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
