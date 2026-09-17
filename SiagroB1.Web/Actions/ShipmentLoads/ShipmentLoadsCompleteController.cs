using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Conclui uma carga de remoção (GAC-1175).
/// </summary>
public class ShipmentLoadsCompleteController(
    ShipmentLoadsCompleteService service
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsComplete")]
    public async Task<ActionResult> Complete(ODataActionParameters parameters)
    {
        try
        {
            if (parameters == null || !parameters.TryGetValue("Key", out var keyObj) || keyObj == null)
            {
                return BadRequest("Missing required parameters");
            }

            var key = Guid.Parse(keyObj.ToString()!);
            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync(key, userName);

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound();
            }

            return BadRequest(e.Message);
        }
    }
}
