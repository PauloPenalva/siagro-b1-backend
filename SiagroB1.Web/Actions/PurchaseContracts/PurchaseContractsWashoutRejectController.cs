using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsWashoutRejectController(
    PurchaseContractsWashoutRejectService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsWashoutReject")]
    public async Task<IActionResult> RejectAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
                return BadRequest("Missing required parameters");

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
