using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Notifications;

public class NotificationOutboxResendController(NotificationOutboxResendService service)
    : ODataController
{
    [HttpPost("odata/NotificationOutboxResend")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // TryGetValue devolve true com valor nulo quando o parâmetro vem sem conteúdo:
            // checar só o retorno deixaria o ToString() estourar com NullReferenceException.
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Missing required parameters");

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), userName);

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
