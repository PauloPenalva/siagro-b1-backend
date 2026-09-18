using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Exclui um anexo da carga (GAC-1171). O serviço barra o anexo que ainda esteja vinculado a um
/// ticket de descarga, e a mensagem dele chega à tela como 400.
/// </summary>
public class ShipmentLoadsAttachmentDeleteController(
    ShipmentLoadAttachmentsDeleteService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsAttachmentDelete")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado.
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Anexo não informado.");

            await service.ExecuteAsync((Guid) keyObj, User.Identity?.Name ?? "Unknown");

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
