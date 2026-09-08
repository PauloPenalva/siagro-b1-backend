using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>O parâmetro é a chave da BAIXA, não a do documento.</summary>
public class FinancialDocumentsReverseSettlementController(
    FinancialDocumentsReverseSettlementService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsReverseSettlement")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("SettlementKey", out var keyObj) || keyObj is null)
                return BadRequest("Informe a baixa a estornar.");

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
