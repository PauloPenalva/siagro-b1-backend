using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>"Compensar com firme" (spec 2026-10-13, regra 20): Key = a devolução; CounterpartKey = o firme solto.</summary>
public class FinancialDocumentsNettingController(FinancialDocumentsNettingService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsNetting")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título de devolução.");

            if (!parameters.TryGetValue("CounterpartKey", out var counterpartObj) || counterpartObj is null)
                return BadRequest("Informe o título firme a compensar.");

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!), Guid.Parse(counterpartObj.ToString()!), User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
