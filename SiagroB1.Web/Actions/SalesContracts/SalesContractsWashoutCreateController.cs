using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Actions.PurchaseContracts;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsWashoutCreateController(
    SalesContractsWashoutCreateService service) : ODataController
{
    [HttpPost("odata/SalesContractsWashoutCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ODataActionParameters vem NULO quando o corpo não casa com o EDM.
            if (parameters is null
                || !parameters.TryGetValue("SalesContractKey", out var keyObj) || keyObj is not Guid contractKey)
                return BadRequest("Missing required parameters");

            if (!parameters.TryGetValue("Washout", out var washoutObj) || washoutObj is not SalesContractWashout washout)
                return BadRequest("Missing washout payload");

            await service.ExecuteAsync(contractKey, washout, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
