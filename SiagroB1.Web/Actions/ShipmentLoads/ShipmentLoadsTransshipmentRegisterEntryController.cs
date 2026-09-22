using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Registra a entrada do transbordo da carga (GAC-1181) no armazém intermediário: em armazém
/// próprio aponta o romaneio de Entrada em Armazenagem já lançado (ReceiptStorageTransactionKey);
/// em armazém de terceiro informa o peso pesado (GrossWeight) e o serviço monta o romaneio 15.
/// </summary>
public class ShipmentLoadsTransshipmentRegisterEntryController(
    ShipmentLoadsTransshipmentRegisterEntryService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsTransshipmentRegisterEntry")]
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

            parameters.TryGetValue("EntryDate", out var dateObj);
            parameters.TryGetValue("GrossWeight", out var grossWeightObj);
            parameters.TryGetValue("ReceiptStorageTransactionKey", out var receiptKeyObj);

            // ⚠️ TryParseExact, nunca TryParse — ver ShipmentLoadActionParameters.
            if (!ShipmentLoadActionParameters.TryParseDate(dateObj, out var parsedDate))
                return BadRequest(ShipmentLoadActionParameters.InvalidDateMessage);

            if (parsedDate is null)
                return BadRequest(ShipmentLoadActionParameters.MissingDateMessage);

            var userName = User.Identity?.Name ?? "Unknown";

            // Peso único: não existe parâmetro de tara aqui, como não existe no romaneio
            // (só GrossWeight/NetWeight). Ausente vale zero — o serviço decide se é aceitável
            // (armazém próprio deriva o peso do romaneio apontado por ReceiptStorageTransactionKey).
            await service.ExecuteAsync(
                (Guid) keyObj,
                Convert.ToDecimal(grossWeightObj ?? 0d, CultureInfo.InvariantCulture),
                parsedDate.Value,
                receiptKeyObj as Guid?,
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
