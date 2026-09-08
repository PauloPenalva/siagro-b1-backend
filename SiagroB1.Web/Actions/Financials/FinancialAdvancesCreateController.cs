using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialAdvancesCreateController(FinancialAdvancesCreateService service) : ODataController
{
    [HttpPost("odata/FinancialAdvancesCreate")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("ContractType", out var typeObj) || typeObj is null)
                return BadRequest("Informe o tipo de contrato.");

            if (!parameters.TryGetValue("ContractKey", out var contractObj) || contractObj is null)
                return BadRequest("Informe o contrato.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor do adiantamento.");

            if (!parameters.TryGetValue("DueDate", out var dueObj) || dueObj is null)
                return BadRequest("Informe o vencimento do adiantamento.");

            parameters.TryGetValue("Comments", out var commentsObj);

            var advance = await service.ExecuteAsync(
                contractType: typeObj.ToString()!,
                contractKey: Guid.Parse(contractObj.ToString()!),
                amount: Convert.ToDecimal(amountObj),
                dueDate: DateTime.Parse(dueObj.ToString()!),
                comments: commentsObj?.ToString(),
                userName: User.Identity?.Name ?? "Unknown");

            return Ok(new { advance.Key, advance.Code });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
