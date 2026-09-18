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

            if (!ShipmentLoadActionParameters.TryParseDate(dateObj, out var parsedDate))
                return BadRequest(ShipmentLoadActionParameters.InvalidDateMessage);

            // ⚠️ Na alteração a data é OBRIGATÓRIA, ao contrário do registro.
            // ShipmentLoadDischargesUpdateService grava DischargeDate sem condição e não há guard
            // de data nas regras: cair no dia de hoje carimbaria HOJE por cima da data já
            // registrada, sem erro e sem log — o usuário só descobriria conferindo o ticket.
            if (parsedDate is null)
                return BadRequest(ShipmentLoadActionParameters.MissingDateMessage);

            await service.ExecuteAsync(
                (Guid) keyObj,
                ticketObj as string,
                parsedDate.Value,
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
}
