using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseDischarges;

namespace SiagroB1.Web.Functions.PurchaseDischarges;

public class PurchaseDischargesListController(PurchaseDischargesListService service) : ODataController
{
    [HttpGet("odata/PurchaseDischargesList(DateFrom={dateFrom},DateTo={dateTo})")]
    public async Task<ActionResult> List([FromRoute] string? dateFrom, [FromRoute] string? dateTo)
    {
        // Parâmetro string do OData é anulável e chega com as aspas: TryParseExact estrito, sem
        // "hoje" como fallback (lição do GAC-1171).
        if (!TryParse(dateFrom, out var from) || !TryParse(dateTo, out var to))
            return BadRequest("Informe o período no formato aaaa-mm-dd.");

        return Ok(await service.ExecuteAsync(from, to));
    }

    private static bool TryParse(string? raw, out DateTime value) =>
        DateTime.TryParseExact(raw?.Trim('\''), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value);
}
