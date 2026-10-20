using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialAccountTransfersCreateController(FinancialAccountTransfersCreateService service) : ODataController
{
    [HttpPost("odata/FinancialAccountTransfersCreate")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("TransferDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data da transferência.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor da transferência.");

            var created = await service.ExecuteAsync(new FinancialAccountTransfer
            {
                FromAccountCode = Text(parameters, "FromAccountCode") ?? string.Empty,
                ToAccountCode = Text(parameters, "ToAccountCode") ?? string.Empty,
                TransferDate = DateTime.Parse(dateObj.ToString()!),
                Amount = Convert.ToDecimal(amountObj),
                Description = Text(parameters, "Description") ?? string.Empty,
                Notes = Text(parameters, "Notes")
            }, User.Identity?.Name ?? "Unknown");

            return Ok(created.Key);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }

    private static string? Text(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var v) ? v?.ToString() : null;
}
