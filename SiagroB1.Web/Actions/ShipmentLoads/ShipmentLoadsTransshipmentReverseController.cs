using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Estorna o transbordo da carga (GAC-1181) — desfaz a entrada registrada, ou só remove a linha
/// se a entrada ainda não tiver sido registrada — e devolve o volume à carga.
/// </summary>
public class ShipmentLoadsTransshipmentReverseController(
    ShipmentLoadsTransshipmentReverseService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsTransshipmentReverse")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ⚠️ parameters chega NULO quando falta parâmetro não-opcional do EDM.
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Transbordo não informado.");

            parameters.TryGetValue("Reason", out var reasonObj);

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync((Guid) keyObj, reasonObj as string, userName);

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
