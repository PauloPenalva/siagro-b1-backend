using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Inicia o transbordo da carga (GAC-1181): descarrega o saldo INTEIRO disponível no armazém
/// intermediário informado.
/// </summary>
public class ShipmentLoadsTransshipmentStartController(
    ShipmentLoadsTransshipmentStartService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsTransshipmentStart")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ⚠️ parameters chega NULO quando falta parâmetro não-opcional do EDM — a guarda
            // vem antes de tudo, senão o TryGetValue seguinte estoura NRE e vira 500 de corpo
            // vazio.
            if (parameters is null ||
                !parameters.TryGetValue("LoadKey", out var loadKeyObj) || loadKeyObj is null ||
                !parameters.TryGetValue("WarehouseCode", out var warehouseCodeObj) || warehouseCodeObj is null)
                return BadRequest("Carga e armazém de destino são obrigatórios.");

            parameters.TryGetValue("TransshipmentDate", out var dateObj);
            parameters.TryGetValue("Comments", out var commentsObj);

            // ⚠️ TryParseExact, nunca TryParse — ver ShipmentLoadActionParameters.
            if (!ShipmentLoadActionParameters.TryParseDate(dateObj, out var parsedDate))
                return BadRequest(ShipmentLoadActionParameters.InvalidDateMessage);

            if (parsedDate is null)
                return BadRequest(ShipmentLoadActionParameters.MissingDateMessage);

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync(
                (Guid) loadKeyObj,
                (string) warehouseCodeObj,
                parsedDate.Value,
                commentsObj as string,
                userName);

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
