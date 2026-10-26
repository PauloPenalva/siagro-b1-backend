using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsSetEarlyPaymentAuthorizedController(
    PurchaseContractsSetPaymentReleaseStatusService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsSetEarlyPaymentAuthorized")]
    public async Task<IActionResult> SetEarlyPaymentAuthorizedAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // O OData entrega NULO aqui quando o corpo não traz nenhum parâmetro declarado no EDM.
            if (parameters is null
                || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null
                || !parameters.TryGetValue("Authorized", out var authObj) || authObj is null)
                return BadRequest("Parâmetros obrigatórios não informados.");

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString()!);
            var authorized = Convert.ToBoolean(authObj);
            await service.SetEarlyPaymentAuthorizedAsync(key, authorized, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
