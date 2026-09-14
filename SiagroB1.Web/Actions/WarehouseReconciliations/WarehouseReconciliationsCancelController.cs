using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WarehouseReconciliations;

public class WarehouseReconciliationsCancelController(
    WarehouseReconciliationsCancelService service) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsCancel")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Key é obrigatório.");

        // String OData é anulável: TryGetValue devolve true com null.
        parameters.TryGetValue("Reason", out var reasonObj);

        try
        {
            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!), reasonObj?.ToString(), User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e) when (e is NotFoundException or KeyNotFoundException)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
