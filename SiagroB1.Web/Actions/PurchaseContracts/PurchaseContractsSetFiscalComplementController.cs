using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsSetFiscalComplementController(PurchaseContractFiscalComplementService service)
    : ODataController
{
    /// <remarks>
    /// <paramref name="parameters"/> vem NULO sem nenhum parâmetro do EDM, e <c>UsageCode</c> presente pode vir
    /// nulo (limpar é válido). Código numérico chega como int; nada de <c>.ToString()</c> direto.
    /// </remarks>
    [HttpPost("odata/PurchaseContractsSetFiscalComplement")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) ||
                keyObj is not Guid key)
            {
                return BadRequest("Missing required parameters");
            }

            parameters.TryGetValue("UsageCode", out var usageObj);

            var result = await service.SetAsync(
                key, usageObj is null ? null : Convert.ToInt32(usageObj), User.Identity?.Name ?? "Unknown");

            return Ok(result);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}
