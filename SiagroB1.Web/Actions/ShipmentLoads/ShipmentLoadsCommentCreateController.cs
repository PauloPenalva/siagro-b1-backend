using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsCommentCreateController(
    ShipmentLoadsCommentCreateService service) : ODataController
{
    /// <summary><c>LoadKey</c> é a chave da CARGA — as outras duas actions recebem a do comentário.</summary>
    [HttpPost("odata/ShipmentLoadsCommentCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // parameters chega NULO quando nenhum parametro do EDM e enviado.
            if (parameters is null || !parameters.TryGetValue("LoadKey", out var keyObj))
                return BadRequest("Chave da carga não informada.");

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
