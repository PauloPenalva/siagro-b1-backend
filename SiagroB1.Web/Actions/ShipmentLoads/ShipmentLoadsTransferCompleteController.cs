using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Conclui a transferência entre armazéns de terceiros (GAC-1176): a carga termina no armazém do
/// transbordo e encerra sem faturamento. <c>Key</c> é a chave do TRANSBORDO.
/// </summary>
public class ShipmentLoadsTransferCompleteController(
    ShipmentLoadsTransshipmentCompleteService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsTransferComplete")]
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

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync((Guid) keyObj, userName);

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
