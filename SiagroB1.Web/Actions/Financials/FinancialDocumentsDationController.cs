using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Dação da sobra do firme (spec 2026-10-13, regra 17). Amount é Edm.Double; SettlementDate, string ISO.</summary>
public class FinancialDocumentsDationController(FinancialDocumentsDationService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsDation")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título firme.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor da dação.");

            if (!parameters.TryGetValue("SettlementDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data da dação.");

            // TryGetValue devolve TRUE com valor null em parâmetro presente: sempre value?.ToString().
            parameters.TryGetValue("Notes", out var notesObj);

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                Convert.ToDecimal(amountObj),
                DateTime.Parse(dateObj.ToString()!),
                notesObj?.ToString(),
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
