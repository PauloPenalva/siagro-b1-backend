using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsWashoutCreateController(
    PurchaseContractsWashoutCreateService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsWashoutCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ODataActionParameters vem NULO quando o corpo não casa com o EDM.
            if (parameters is null
                || !parameters.TryGetValue("PurchaseContractKey", out var keyObj) || keyObj is not Guid contractKey)
                return BadRequest("Missing required parameters");

            if (!parameters.TryGetValue("Washout", out var washoutObj) || washoutObj is not PurchaseContractWashout washout)
                return BadRequest("Missing washout payload");

            await service.ExecuteAsync(contractKey, washout, User.Identity?.Name ?? "Unknown");

            // Sem corpo, como a criação de fixação: a tela recarrega a lista.
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
