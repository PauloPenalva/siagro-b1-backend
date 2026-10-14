using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>
/// "Compensar adiantamento" no provisório (spec 2026-10-14, regras 1–4). Key = provisório; AdvanceKey = adiantamento;
/// Amount é Edm.Double; SettlementDate, string ISO yyyy-MM-dd. Recusas de negócio voltam como 400 com o texto.
/// </summary>
public class FinancialDocumentsApplyAdvanceController(FinancialDocumentsApplyAdvanceService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsApplyAdvance")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título provisório.");

            if (!parameters.TryGetValue("AdvanceKey", out var advanceObj) || advanceObj is null)
                return BadRequest("Informe o adiantamento.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor a compensar.");

            if (!parameters.TryGetValue("SettlementDate", out var dateObj) || string.IsNullOrWhiteSpace(dateObj?.ToString()))
                return BadRequest("Informe a data da compensação.");

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                Guid.Parse(advanceObj.ToString()!),
                Convert.ToDecimal(amountObj, CultureInfo.InvariantCulture),
                DateTime.Parse(dateObj!.ToString()!, CultureInfo.InvariantCulture),
                User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
