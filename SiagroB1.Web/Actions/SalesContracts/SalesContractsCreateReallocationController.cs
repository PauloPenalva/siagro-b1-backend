using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsCreateReallocationController(SalesContractsReallocationCreateService service)
    : ODataController
{
    [HttpPost("odata/SalesContractsCreateReallocation")]
    public async Task<IActionResult> CreateAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("SalesInvoiceItemKey", out var itemKeyObj) ||
                !parameters.TryGetValue("SourceSalesContractKey", out var sourceKeyObj) ||
                !parameters.TryGetValue("TargetSalesContractKey", out var targetKeyObj) ||
                !parameters.TryGetValue("TargetSalesShipmentReleaseKey", out var targetReleaseKeyObj) ||
                !parameters.TryGetValue("Volume", out var volumeObj))
            {
                return BadRequest("Missing required parameters");
            }

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteWithTransactionAsync(
                Guid.Parse(itemKeyObj.ToString()!),
                Guid.Parse(sourceKeyObj.ToString()!),
                Guid.Parse(targetKeyObj.ToString()!),
                Guid.Parse(targetReleaseKeyObj.ToString()!),
                Convert.ToDecimal(volumeObj),
                userName);

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }

            if (e is ApplicationException or DefaultException or FormatException)
            {
                return BadRequest(e.Message);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}
