using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Vincula o adiantamento a outro contrato do MESMO parceiro.</summary>
public class FinancialAdvancesRelinkContractController(
    FinancialAdvancesRelinkContractService service) : ODataController
{
    [HttpPost("odata/FinancialAdvancesRelinkContract")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("DocumentKey", out var keyObj) || keyObj is null)
                return BadRequest("Informe o adiantamento.");

            if (!parameters.TryGetValue("ContractType", out var typeObj) || typeObj is null)
                return BadRequest("Informe o tipo do contrato de destino.");

            if (!parameters.TryGetValue("ContractKey", out var contractObj) || contractObj is null)
                return BadRequest("Informe o contrato de destino.");

            await service.ExecuteAsync(
                documentKey: Guid.Parse(keyObj.ToString()!),
                targetContractType: typeObj.ToString()!,
                targetContractKey: Guid.Parse(contractObj.ToString()!),
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
