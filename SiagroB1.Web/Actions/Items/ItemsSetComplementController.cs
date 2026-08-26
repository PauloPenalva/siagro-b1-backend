using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Actions.Items;

public class ItemsSetComplementController(IItemComplementService service)
    : ODataController
{
    /// <remarks>
    /// <paramref name="parameters"/> vem NULO quando o corpo não traz nenhum parâmetro do EDM —
    /// tocar em <c>TryGetValue</c> antes de testar isso é NullReferenceException, ou seja, 500 de
    /// corpo vazio. E parâmetro opcional presente pode vir com valor nulo: os dois campos do
    /// complemento são opcionais de propósito (limpar o complemento é operação válida), então
    /// nada de <c>.ToString()</c> direto.
    /// </remarks>
    [HttpPost("odata/ItemsSetComplement")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("ItemCode", out var itemCodeObj) ||
                itemCodeObj is null)
            {
                return BadRequest("Missing required parameters");
            }

            parameters.TryGetValue("CommercialUnitOfMeasureCode", out var uomObj);
            parameters.TryGetValue("CommercialFactor", out var factorObj);

            var result = await service.SetAsync(
                itemCodeObj.ToString()!,
                uomObj?.ToString(),
                factorObj is null ? null : Convert.ToDecimal(factorObj));

            return Ok(result);
        }
        catch (Exception e)
        {
            if (e is DefaultException)
            {
                return BadRequest(e.Message);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}
