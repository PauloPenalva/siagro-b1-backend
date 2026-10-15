using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Web.Actions.PurchaseContracts;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsWashoutRejectController(
    SalesContractsWashoutRejectService service) : ODataController
{
    [HttpPost("odata/SalesContractsWashoutReject")]
    public async Task<IActionResult> RejectAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
                return BadRequest("Missing required parameters");

            // Parâmetro string do OData chega null: nunca .ToString() direto.
            parameters.TryGetValue("Comments", out var commentsObj);

            await service.ExecuteAsync(key, commentsObj as string, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
