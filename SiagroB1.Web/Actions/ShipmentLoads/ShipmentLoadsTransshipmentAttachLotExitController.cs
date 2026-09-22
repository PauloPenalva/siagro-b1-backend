using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Vincula ao transbordo (GAC-1181 fase 2) a saída do LOTE em armazém PRÓPRIO — o romaneio
/// <c>Shipment</c> pesado ao recarregar o grão de volta para o cliente. É este vínculo que emite
/// a liberação pela quantidade REAL carregada; a entrada (Task 3) só credita o armazém.
/// </summary>
public class ShipmentLoadsTransshipmentAttachLotExitController(
    ShipmentLoadsTransshipmentAttachLotExitService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsTransshipmentAttachLotExit")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ⚠️ parameters chega NULO quando falta parâmetro não-opcional do EDM.
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Transbordo não informado.");

            if (!parameters.TryGetValue("LotExitStorageTransactionKey", out var lotExitKeyObj) ||
                lotExitKeyObj is null)
                return BadRequest("Romaneio de saída do lote não informado.");

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync((Guid) keyObj, (Guid) lotExitKeyObj, userName);

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
