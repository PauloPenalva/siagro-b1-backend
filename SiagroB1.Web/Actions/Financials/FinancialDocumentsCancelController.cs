using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsCancelController(FinancialDocumentsCancelService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsCancel")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o documento financeiro.");

            parameters.TryGetValue("Reason", out var reasonObj);

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                reasonObj?.ToString() ?? string.Empty,
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
