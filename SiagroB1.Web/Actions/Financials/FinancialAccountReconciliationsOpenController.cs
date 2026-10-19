using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialAccountReconciliationsOpenController(FinancialAccountReconciliationsOpenService service)
    : ODataController
{
    [HttpPost("odata/FinancialAccountReconciliationsOpen")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("StatementDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data do extrato.");

            if (!parameters.TryGetValue("StatementBalance", out var balanceObj) || balanceObj is null)
                return BadRequest("Informe o saldo do extrato.");

            var created = await service.ExecuteAsync(
                parameters.TryGetValue("FinancialAccountCode", out var a) ? a?.ToString() ?? string.Empty : string.Empty,
                DateTime.Parse(dateObj.ToString()!),
                Convert.ToDecimal(balanceObj),
                parameters.TryGetValue("Notes", out var n) ? n?.ToString() : null,
                User.Identity?.Name ?? "Unknown");

            return Ok(created.Key);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
