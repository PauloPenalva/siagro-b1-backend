using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>
/// Devolução do valor adiantado. O VALOR não vem da tela: é sempre o saldo baixado, lido no
/// servidor.
/// </summary>
public class FinancialAdvancesRefundController(FinancialAdvancesRefundService service) : ODataController
{
    [HttpPost("odata/FinancialAdvancesRefund")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("DocumentKey", out var keyObj) || keyObj is null)
                return BadRequest("Informe o adiantamento a devolver.");

            if (!parameters.TryGetValue("FinancialAccountCode", out var accountObj) || accountObj is null)
                return BadRequest("Informe a conta financeira que recebeu a devolução.");

            if (!parameters.TryGetValue("RefundDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data da devolução.");

            // TryGetValue devolve TRUE com valor null em parâmetro opcional.
            parameters.TryGetValue("DocumentReference", out var referenceObj);
            parameters.TryGetValue("Reason", out var reasonObj);

            await service.ExecuteAsync(
                documentKey: Guid.Parse(keyObj.ToString()!),
                financialAccountCode: accountObj.ToString()!,
                refundDate: DateTime.Parse(dateObj.ToString()!),
                documentReference: referenceObj?.ToString(),
                reason: reasonObj?.ToString() ?? string.Empty,
                userName: User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
