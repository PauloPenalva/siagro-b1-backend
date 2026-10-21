using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialAccountEntriesCreateController(FinancialAccountEntriesCreateService service) : ODataController
{
    [HttpPost("odata/FinancialAccountEntriesCreate")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("EntryDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data do lançamento.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor do lançamento.");

            var created = await service.ExecuteAsync(new FinancialAccountEntry
            {
                FinancialAccountCode = Text(parameters, "FinancialAccountCode") ?? string.Empty,
                EntryDate = DateTime.Parse(dateObj.ToString()!),
                Amount = Convert.ToDecimal(amountObj),
                Description = Text(parameters, "Description") ?? string.Empty,
                DocumentReference = Text(parameters, "DocumentReference"),
                Notes = Text(parameters, "Notes"),
                CostCenterCode = Text(parameters, "CostCenterCode"),
                AccountingItemCode = Text(parameters, "AccountingItemCode"),
                DistributionRuleCode = Text(parameters, "DistributionRuleCode"),
                LedgerAccountCode = Text(parameters, "LedgerAccountCode")
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
