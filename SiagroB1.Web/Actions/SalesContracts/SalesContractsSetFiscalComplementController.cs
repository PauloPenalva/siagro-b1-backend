using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsSetFiscalComplementController(SalesContractFiscalComplementService service)
    : ODataController
{
    /// <remarks>
    /// <paramref name="parameters"/> vem NULO sem nenhum parâmetro do EDM, e os opcionais presentes podem vir nulos
    /// (limpar um campo é válido). Códigos numéricos chegam como int; nada de <c>.ToString()</c> direto.
    /// </remarks>
    [HttpPost("odata/SalesContractsSetFiscalComplement")]
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
            parameters.TryGetValue("PaymentConditionCode", out var conditionObj);
            parameters.TryGetValue("AdditionalInfo", out var infoObj);
            parameters.TryGetValue("CustomerOrderNumber", out var orderObj);
            parameters.TryGetValue("CustomerOrderItem", out var itemObj);

            var input = new SalesContractFiscalComplementInput(
                usageObj is null ? null : Convert.ToInt32(usageObj),
                conditionObj is null ? null : Convert.ToInt32(conditionObj),
                infoObj?.ToString(),
                orderObj?.ToString(),
                itemObj?.ToString());

            var result = await service.SetAsync(key, input, User.Identity?.Name ?? "Unknown");

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
