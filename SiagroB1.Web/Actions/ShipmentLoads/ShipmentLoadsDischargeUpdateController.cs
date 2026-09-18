using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Altera um ticket de descarga já registrado (GAC-1171). Nota e item não viajam nesta action:
/// apontar o ticket para outra linha é excluir e registrar de novo — ver o serviço.
/// </summary>
public class ShipmentLoadsDischargeUpdateController(
    ShipmentLoadDischargesUpdateService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsDischargeUpdate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado.
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Registro de descarga não informado.");

            parameters.TryGetValue("TicketNumber", out var ticketObj);
            parameters.TryGetValue("DischargeDate", out var dateObj);
            parameters.TryGetValue("Quantity", out var quantityObj);
            parameters.TryGetValue("Comments", out var commentsObj);

            await service.ExecuteAsync(
                (Guid) keyObj,
                ticketObj as string,
                ParseDate(dateObj),
                Convert.ToDecimal(quantityObj ?? 0d, CultureInfo.InvariantCulture),
                commentsObj as string,
                User.Identity?.Name ?? "Unknown");

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

    /// <summary>
    /// A data viaja como string "yyyy-MM-dd". Parâmetro string do EDM é anulável, então o nulo
    /// cai no dia de hoje em vez de estourar.
    /// </summary>
    private static DateTime ParseDate(object? value) =>
        value is string text && DateTime.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : DateTime.Now.Date;
}
