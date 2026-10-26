using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>GAC-1185: <c>ContractType</c> = "Purchase" | "Sales"; <c>Quantity</c> (Double) e <c>Notes</c> opcionais.</summary>
public class ShipmentLoadsPlannedContractAddController(
    ShipmentLoadsPlannedContractAddService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsPlannedContractAdd")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // parameters chega NULO quando nenhum parametro do EDM e enviado.
            if (parameters is null ||
                !parameters.TryGetValue("LoadKey", out var loadKey) ||
                !parameters.TryGetValue("ContractKey", out var contractKey))
                return BadRequest("Informe a carga e o contrato.");

            parameters.TryGetValue("ContractType", out var typeObj);
            parameters.TryGetValue("Quantity", out var quantityObj);
            parameters.TryGetValue("Notes", out var notesObj);

            if (!ShipmentLoadActionParameters.TryParseContractType(typeObj, out var type))
                return BadRequest(ShipmentLoadActionParameters.InvalidContractTypeMessage);

            if (!ShipmentLoadActionParameters.TryReadOptionalQuantity(quantityObj, out var quantity))
                return BadRequest("Quantidade planejada inválida.");

            await service.ExecuteAsync(
                (Guid) loadKey, type, (Guid) contractKey, quantity, notesObj as string,
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
