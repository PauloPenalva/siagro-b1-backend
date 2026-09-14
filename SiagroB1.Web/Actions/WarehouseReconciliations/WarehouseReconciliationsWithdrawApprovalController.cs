using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WarehouseReconciliations;

public class WarehouseReconciliationsWithdrawApprovalController(
    WarehouseReconciliationsWithdrawApprovalService service) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsWithdrawApproval")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Key é obrigatório.");

        try
        {
            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), User.Identity?.Name ?? "Unknown");
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
