using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsCancelController(
    ShipmentLoadsCancelService cancelService
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsCancel")]
    public async Task<ActionResult> Cancel(ODataActionParameters parameters)
    {
        try
        {
            if (parameters == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("CancellationReason", out var reasonObj) ||
                string.IsNullOrWhiteSpace(reasonObj?.ToString()))
            {
                return BadRequest("Informe o motivo do cancelamento.");
            }

            var key = Guid.Parse(keyObj.ToString()!);
            var userName = User.Identity?.Name ?? "Unknown";

            await cancelService.ExecuteAsync(key, reasonObj.ToString()!, userName);
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
