using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Actions.Warehouses;

public class WarehousesSetComplementController(IWarehouseComplementService service)
    : ODataController
{
    /// <remarks>
    /// <paramref name="parameters"/> vem NULO quando o corpo não traz nenhum parâmetro do EDM —
    /// tocar em <c>TryGetValue</c> antes de testar isso é NullReferenceException, ou seja, 500 de
    /// corpo vazio. Os dois flags são opcionais no EDM e ausentes valem <c>false</c>, que é o
    /// mesmo significado de "não existe registro de complemento".
    /// </remarks>
    [HttpPost("odata/WarehousesSetComplement")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("WarehouseCode", out var warehouseCodeObj) ||
                warehouseCodeObj is null)
            {
                return BadRequest("Missing required parameters");
            }

            parameters.TryGetValue("IsParticipant", out var isParticipantObj);
            parameters.TryGetValue("IsOwn", out var isOwnObj);
            // Parametro string opcional volta true no TryGetValue com valor NULO: nada de
            // .ToString() direto aqui.
            parameters.TryGetValue("Notes", out var notesObj);

            var result = await service.SetAsync(
                warehouseCodeObj.ToString()!,
                isParticipantObj is not null && Convert.ToBoolean(isParticipantObj),
                isOwnObj is not null && Convert.ToBoolean(isOwnObj),
                notesObj?.ToString());

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
