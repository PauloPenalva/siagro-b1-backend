using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsAttachTransactionsController(
    ShipmentLoadsAttachTransactionsService attachService
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsAttachTransactions")]
    public async Task<ActionResult> Attach(ODataActionParameters parameters)
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

            if (!parameters.TryGetValue("StorageTransactionKeys", out var keysObj) ||
                keysObj is not IEnumerable<Guid> keys)
            {
                return BadRequest("Selecione ao menos um romaneio de embarque para vincular.");
            }

            var key = Guid.Parse(keyObj.ToString()!);
            var userName = User.Identity?.Name ?? "Unknown";

            var load = await attachService.ExecuteAsync(key, keys.ToList(), userName);

            return Ok(new { load.Key, load.Code, load.TotalQuantity, load.Status });
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
