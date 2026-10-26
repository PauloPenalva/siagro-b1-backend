using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>GAC-1185: <c>Key</c> é a chave da LINHA planejada, não a do contrato.</summary>
public class ShipmentLoadsPlannedContractRemoveController(
    ShipmentLoadsPlannedContractRemoveService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsPlannedContractRemove")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var key))
                return BadRequest("Chave do contrato planejado não informada.");

            parameters.TryGetValue("ContractType", out var typeObj);

            if (!ShipmentLoadActionParameters.TryParseContractType(typeObj, out var type))
                return BadRequest(ShipmentLoadActionParameters.InvalidContractTypeMessage);

            await service.ExecuteAsync(type, (Guid) key, User.Identity?.Name ?? "Unknown");

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
