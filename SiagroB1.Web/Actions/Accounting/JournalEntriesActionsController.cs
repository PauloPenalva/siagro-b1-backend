using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Actions.Accounting;

/// <summary>Estorno de lançamento contábil (action de topo). Só STANDALONE.</summary>
public class JournalEntriesActionsController(IJournalEntriesService service, IConfiguration configuration) : ODataController
{
    [HttpPost("odata/JournalEntriesReverse")]
    public async Task<IActionResult> ReverseAsync(ODataActionParameters? parameters)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest("Disponível somente no modo STANDALONE.");
        if (!int.TryParse(Text(parameters, "DocEntry"), out var docEntry)) return BadRequest("Informe o lançamento.");
        var date = DateTime.TryParse(Text(parameters, "Date"), out var d) ? d.Date : DateTime.Today;

        try { return Ok(await service.ReverseAsync(docEntry, date, User.Identity?.Name ?? "Unknown")); }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    private static string? Text(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) ? value?.ToString()?.Trim() : null;
}
