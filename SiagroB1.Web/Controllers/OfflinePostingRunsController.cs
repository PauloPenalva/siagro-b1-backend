using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Accounting.Offline;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Contabilização off-line: lista das execuções com o relatório e o pedido de uma nova. Só STANDALONE.</summary>
public class OfflinePostingRunsController(IOfflinePostingService service, IUnitOfWork db, IConfiguration configuration)
    : ODataController
{
    private const string StandaloneOnly = "Disponível somente no modo STANDALONE.";

    [EnableQuery(MaxExpansionDepth = 1, PageSize = 200)]
    public ActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        return Ok(db.Context.OfflinePostingRuns.AsQueryable());
    }

    [HttpPost("odata/OfflinePostingRequest")]
    public async Task<IActionResult> RequestAsync(ODataActionParameters? parameters, CancellationToken ct)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);
        if (parameters is null) return BadRequest("Informe o período e os módulos.");

        var from = CalendarDate(parameters.GetValueOrDefault("DateFrom"));
        var to = CalendarDate(parameters.GetValueOrDefault("DateTo"));
        if (from is null || to is null) return BadRequest("Informe o período.");
        var modules = parameters.GetValueOrDefault("Modules") is IEnumerable<int> list
            ? list.Select(m => (OfflinePostingModule)m).Where(Enum.IsDefined).ToList()
            : [];
        var simulation = parameters.GetValueOrDefault("Simulation") is true;
        var branch = parameters.GetValueOrDefault("BranchCode")?.ToString();

        try
        {
            return Ok(await service.RequestAsync(from.Value, to.Value, branch, modules, simulation,
                User.Identity?.Name ?? "Unknown", ct));
        }
        catch (DefaultException e) { return BadRequest(e.Message); }
    }

    /// <summary>Dia do calendário no próprio offset (ver AccountingSettingsController).</summary>
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
