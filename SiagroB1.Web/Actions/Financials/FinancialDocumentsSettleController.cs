using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsSettleController(FinancialDocumentsSettleService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsSettle")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o documento financeiro.");

            if (!parameters.TryGetValue("FinancialAccountCode", out var accountObj))
                return BadRequest("Informe a conta financeira.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor da baixa.");

            if (!parameters.TryGetValue("SettlementDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data da baixa.");

            await service.ExecuteAsync(
                documentKey: Guid.Parse(keyObj.ToString()!),
                financialAccountCode: accountObj?.ToString() ?? string.Empty,
                amount: Convert.ToDecimal(amountObj),
                settlementDate: DateTime.Parse(dateObj.ToString()!),
                interest: Money(parameters, "InterestAmount"),
                fine: Money(parameters, "FineAmount"),
                discount: Money(parameters, "DiscountAmount"),
                documentReference: Text(parameters, "DocumentReference"),
                notes: Text(parameters, "Notes"),
                userName: User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }

    // TryGetValue devolve TRUE com valor null em parâmetro opcional: sempre value?.ToString().
    private static string? Text(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var v) ? v?.ToString() : null;

    private static decimal Money(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var v) && v is not null ? Convert.ToDecimal(v) : 0m;
}
