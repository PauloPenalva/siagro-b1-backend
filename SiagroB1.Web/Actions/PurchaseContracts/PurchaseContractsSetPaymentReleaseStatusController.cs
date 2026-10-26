using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsSetPaymentReleaseStatusController(
    PurchaseContractsSetPaymentReleaseStatusService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsSetPaymentReleaseStatus")]
    public async Task<IActionResult> SetPaymentReleaseStatusAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // O OData entrega NULO aqui quando o corpo não traz nenhum parâmetro declarado no EDM.
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Parâmetros obrigatórios não informados.");

            // Nulo/vazio é valor legítimo: limpa a situação da liberação de pagamento.
            parameters.TryGetValue("PaymentReleaseStatus", out var statusObj);
            var statusText = statusObj?.ToString();

            PaymentReleaseStatus? status = null;
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                if (!Enum.TryParse<PaymentReleaseStatus>(statusText, out var parsed) || !Enum.IsDefined(parsed))
                    return BadRequest("Situação de liberação de pagamento inválida.");

                status = parsed;
            }

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString()!);
            await service.SetStatusAsync(key, status, userName);
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
