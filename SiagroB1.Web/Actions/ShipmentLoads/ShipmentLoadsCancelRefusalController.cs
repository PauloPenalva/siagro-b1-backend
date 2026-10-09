using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>"Cancelar recusa" da carga com recusa aguardando NF-e (spec 2026-10-09 §5.4).</summary>
public class ShipmentLoadsCancelRefusalController(ShipmentLoadsCancelRefusalService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsCancelRefusal")]
    public async Task<ActionResult> Cancel(ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta parâmetro declarado: sem a guarda, NRE = 500 vazio.
        if (parameters == null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
            return BadRequest("Missing required parameters");

        try
        {
            var load = await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown");
            return Ok(new { load.Key, load.Code, Status = load.Status.ToString() });
        }
        catch (DbUpdateConcurrencyException)
        {
            return BadRequest(
                "A carga foi alterada por outro usuário enquanto a recusa era cancelada. " +
                "Reabra a tela e tente novamente.");
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
