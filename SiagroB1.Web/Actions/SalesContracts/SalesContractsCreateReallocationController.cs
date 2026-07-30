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
                !parameters.TryGetValue("Volume", out var volumeObj))
            {
                return BadRequest("Missing required parameters");
            }

            // Parâmetros OPCIONAIS. Atenção: TryGetValue devolve true com valor null
            // quando o cliente manda a chave com null — sem o `is not null` o ToString()
            // estoura. A ausência da liberação é o que seleciona o modo CONCILIAÇÃO.
            Guid? targetReleaseKey =
                parameters.TryGetValue("TargetSalesShipmentReleaseKey", out var targetReleaseKeyObj)
                && targetReleaseKeyObj is not null
                    ? Guid.Parse(targetReleaseKeyObj.ToString()!)
                    : null;

            var reason = parameters.TryGetValue("Reason", out var reasonObj)
                ? reasonObj?.ToString()
                : null;

            var allowNegativeBalance =
                parameters.TryGetValue("AllowNegativeBalance", out var allowObj)
                && allowObj is not null
                && Convert.ToBoolean(allowObj);

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteWithTransactionAsync(
                Guid.Parse(itemKeyObj.ToString()!),
                Guid.Parse(sourceKeyObj.ToString()!),
                Guid.Parse(targetKeyObj.ToString()!),
                targetReleaseKey,
                Convert.ToDecimal(volumeObj),
                userName,
                reason,
                allowNegativeBalance);

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
