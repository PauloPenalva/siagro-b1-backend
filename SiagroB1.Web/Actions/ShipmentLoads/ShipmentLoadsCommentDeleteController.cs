using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsCommentDeleteController(
    ShipmentLoadsCommentDeleteService service) : ODataController
{
    /// <summary><c>Key</c> é a chave do COMENTÁRIO, não a da carga.</summary>
    [HttpPost("odata/ShipmentLoadsCommentDelete")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Chave do comentário não informada.");

            await service.ExecuteAsync(
                (Guid) keyObj, User.Identity?.Name ?? "Unknown", User.IsAdmin());

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
