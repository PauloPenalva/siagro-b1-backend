using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.FiscalStock;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Actions.FiscalStock;

/// <summary>Fechar/reabrir o mês do estoque fiscal. Action de topo; os parâmetros podem chegar nulos. Só STANDALONE.</summary>
public class FiscalStockPeriodsActionsController(IFiscalStockPeriodsService service, IConfiguration configuration)
    : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [HttpPost("odata/FiscalStockPeriodsClose")]
    public async Task<IActionResult> CloseAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (Month(parameters) is not var (branch, year, month)) return BadRequest("Informe a filial e o mês.");

        try { return Ok(await service.CloseAsync(branch, year, month, User.Identity?.Name ?? "Unknown")); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    [HttpPost("odata/FiscalStockPeriodsReopen")]
    public async Task<IActionResult> ReopenAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (Month(parameters) is not var (branch, year, month)) return BadRequest("Informe a filial e o mês.");

        try
        {
            return Ok(await service.ReopenAsync(branch, year, month, Text(parameters, "Reason"),
                User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    private static (string Branch, int Year, int Month)? Month(ODataActionParameters? parameters) =>
        Text(parameters, "BranchCode") is { Length: > 0 } branch &&
        int.TryParse(Text(parameters, "Year"), out var year) &&
        int.TryParse(Text(parameters, "Month"), out var month)
            ? (branch, year, month)
            : null;

    private static string? Text(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
