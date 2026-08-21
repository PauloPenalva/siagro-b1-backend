using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsCreateController(
    ShipmentLoadsCreateService createService
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsCreate")]
    public async Task<ActionResult> Create(ODataActionParameters parameters)
    {
        try
        {
            // parameters vem NULO quando o corpo não casa com nenhum parâmetro do EDM —
            // sem esta guarda o TryGetValue estoura NRE e o cliente recebe um 500 de corpo vazio.
            if (parameters == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("StorageTransactionKeys", out var keysObj) ||
                keysObj is not IEnumerable<Guid> keys)
            {
                return BadRequest("Selecione ao menos um romaneio de embarque para montar a carga.");
            }

            // Parâmetro opcional de string vem NULO com true no TryGetValue: nunca chamar
            // .ToString() direto no objeto devolvido.
            parameters.TryGetValue("Comments", out var commentsObj);

            var userName = User.Identity?.Name ?? "Unknown";

            var load = await createService.ExecuteAsync(
                keys.ToList(), commentsObj?.ToString(), userName);

            return Ok(new { load.Key, load.Code });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound();
            }

            return BadRequest(e.Message);
        }
    }
}
