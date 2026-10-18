using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageAddresses;

public class StorageAddressesCloseController(
    StorageAddressesCloseService closeService,
    StorageAddressesReopenService reopenService) : ODataController
{
    [HttpPost("odata/StorageAddressesClose")]
    public async Task<IActionResult> CloseAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters == null ||
                !parameters.TryGetValue("Code", out var code) || code == null ||
                !parameters.TryGetValue("ClosingDate", out var closingDate) || closingDate == null)
                return BadRequest("Informe o lote e a data de encerramento.");

            var userName = User.Identity?.Name ?? "Unknown";
            await closeService.ExecuteAsync(code.ToString()!, ToDateTime(closingDate), userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/StorageAddressesReopen")]
    public async Task<IActionResult> ReopenAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters == null || !parameters.TryGetValue("Code", out var code) || code == null)
                return BadRequest("Informe o lote.");

            var userName = User.Identity?.Name ?? "Unknown";
            await reopenService.ExecuteAsync(code.ToString()!, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }

    // Edm.Date pode chegar como DateOnly ou Microsoft.OData.Edm.Date (ToString() = yyyy-MM-dd).
    private static DateTime ToDateTime(object value) => value switch
    {
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTime d => d,
        DateTimeOffset o => o.Date,
        _ => DateTime.Parse(value.ToString()!, System.Globalization.CultureInfo.InvariantCulture),
    };
}
