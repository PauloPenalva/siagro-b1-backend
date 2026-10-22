using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Apuração de resultado (CTBA211): prévia, apurar e estornar a última. Só STANDALONE.</summary>
public class AccountingResultClosingController(IResultClosingService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [HttpGet("odata/AccountingResultClosingPreview(Date={date},BranchCode={branchCode})")]
    public async Task<IActionResult> PreviewAsync([FromRoute] DateTimeOffset date, [FromRoute] string? branchCode,
        CancellationToken ct)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(await service.UnclosedAsync(date.Date, NullIfEmpty(branchCode), ct));
    }

    [HttpPost("odata/AccountingResultClosingRun")]
    public async Task<IActionResult> RunAsync(ODataActionParameters? parameters, CancellationToken ct)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (CalendarDate(parameters?.GetValueOrDefault("Date")) is not { } date) return BadRequest("Informe a data da apuração.");

        try
        {
            return Ok(await service.CloseAsync(date, Text(parameters, "BranchCode"), Text(parameters, "History"),
                User.Identity?.Name ?? "Unknown", ct));
        }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    [HttpPost("odata/AccountingResultClosingReverse")]
    public async Task<IActionResult> ReverseAsync(ODataActionParameters? parameters, CancellationToken ct)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        try { return Ok(await service.ReverseLatestAsync(Text(parameters, "BranchCode"), User.Identity?.Name ?? "Unknown", ct)); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() is "''" or "null" ? null : value.Trim().Trim('\'');

    private static string? Text(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) ? NullIfEmpty(value?.ToString()) : null;

    private static DateTime? CalendarDate(object? value) => value switch
    {
        DateTimeOffset dto => dto.Date,
        DateTime dt => dt.Date,
        Microsoft.OData.Edm.Date d => new DateTime(d.Year, d.Month, d.Day),
        string s when DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) => parsed.Date,
        _ => null,
    };
}
