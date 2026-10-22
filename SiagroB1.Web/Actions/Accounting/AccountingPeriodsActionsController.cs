using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Actions.Accounting;

/// <summary>Gerar exercício e fechar/bloquear/reabrir mês do calendário contábil. Só STANDALONE.</summary>
public class AccountingPeriodsActionsController(IAccountingPeriodsService service, IConfiguration configuration)
    : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [HttpPost("odata/AccountingPeriodsGenerateYear")]
    public async Task<IActionResult> GenerateYearAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (!int.TryParse(Text(parameters, "Year"), out var year)) return BadRequest("Informe o exercício.");

        try { return Ok(await service.GenerateYearAsync(year, User.Identity?.Name ?? "Unknown")); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    [HttpPost("odata/AccountingPeriodsSetStatus")]
    public async Task<IActionResult> SetStatusAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (!int.TryParse(Text(parameters, "Id"), out var id) ||
            !int.TryParse(Text(parameters, "Status"), out var status) ||
            !Enum.IsDefined(typeof(AccountingPeriodStatus), status))
            return BadRequest("Informe o período e a situação.");

        try
        {
            return Ok(await service.SetStatusAsync(id, (AccountingPeriodStatus)status,
                User.Identity?.Name ?? "Unknown", Text(parameters, "Reason")));
        }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    private static string? Text(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
