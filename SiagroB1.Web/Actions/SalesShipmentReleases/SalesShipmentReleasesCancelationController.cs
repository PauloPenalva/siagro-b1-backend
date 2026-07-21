using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesShipmentReleases;

public class SalesShipmentReleasesCancelationController(
    SalesShipmentReleasesCancelationService cancelationService
    ) : ODataController
{
    [HttpPost("odata/SalesShipmentReleasesCancelation")]
    public async Task<ActionResult> Cancelation(ODataActionParameters parameters)
    {
        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("CancellationReason", out var reasonObj) ||
                string.IsNullOrWhiteSpace(reasonObj?.ToString()))
            {
                return BadRequest("Informe o motivo do cancelamento.");
            }

            var key = Guid.Parse(keyObj.ToString());
            var userName = User.Identity?.Name ?? "Unknown";

            await cancelationService.ExecuteAsync(key, userName, reasonObj.ToString()!);
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
