using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Desfaz a marca de descarregada da carga (GAC-1171, melhorias).
/// </summary>
public class ShipmentLoadsUndoDischargedController(
    ShipmentLoadsUndoDischargedService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsUndoDischarged")]
    public async Task<ActionResult> UndoDischarged([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Informe a carga.");

        try
        {
            await service.ExecuteAsync((Guid)keyObj, User.Identity?.Name ?? "unknown");
            return Ok();
        }
        catch (Exception e)
        {
            // Mesma triagem do upload de anexo: 400 só para erro de negócio. Erro de servidor
            // devolvido como 400 chegaria ao usuário como texto cru em inglês.
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
