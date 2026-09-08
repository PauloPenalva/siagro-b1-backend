using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsGenerateBacklogController(
    FinancialDocumentsGenerateBacklogService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsGenerateBacklog")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("FromDate", out var fromObj) || fromObj is null)
                return BadRequest("Informe a data inicial.");

            if (!parameters.TryGetValue("ToDate", out var toObj) || toObj is null)
                return BadRequest("Informe a data final.");

            // Sem DryRun explícito, simula: gerar por engano é o erro caro aqui.
            var dryRun = !parameters.TryGetValue("DryRun", out var dryObj)
                         || dryObj is null
                         || Convert.ToBoolean(dryObj);

            parameters.TryGetValue("BranchCode", out var branchObj);

            var result = await service.ExecuteAsync(
                branchCode: branchObj?.ToString(),
                fromDate: DateTime.Parse(fromObj.ToString()!),
                toDate: DateTime.Parse(toObj.ToString()!),
                dryRun: dryRun,
                userName: User.Identity?.Name ?? "Unknown");

            return Ok(result);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
