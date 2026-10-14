using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Compor firmes/devoluções no provisório (spec 2026-10-13, regras 9–12). ComposedKeys na ordem do operador.</summary>
public class FinancialDocumentsComposeController(FinancialDocumentsComposeService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsCompose")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título provisório.");

            List<Guid> composedKeys = parameters.TryGetValue("ComposedKeys", out var keysObj) && keysObj is IEnumerable<Guid> keys
                ? keys.ToList()
                : [];

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), composedKeys, User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
