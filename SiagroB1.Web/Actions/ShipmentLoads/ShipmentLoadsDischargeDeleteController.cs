using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Exclui um ticket de descarga (GAC-1171). O ticket excluído fica no log de alterações da carga.
/// </summary>
public class ShipmentLoadsDischargeDeleteController(
    ShipmentLoadDischargesDeleteService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsDischargeDelete")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado.
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Registro de descarga não informado.");

            await service.ExecuteAsync((Guid) keyObj, User.Identity?.Name ?? "Unknown");

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
