using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageDeposits;

/// <summary>"Emitir remessa" na liberação: cria a remessa Pendente e devolve a chave do Documento de Saída.</summary>
public class ShipmentReleasesCreateStorageRemittanceController(ShipmentReleasesCreateStorageRemittanceService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesCreateStorageRemittance")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null
            || !parameters.TryGetValue("ShipmentReleaseKey", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key)
            || !parameters.TryGetValue("UsageCode", out var usageObj) || usageObj is null
            || !parameters.TryGetValue("UnitPrice", out var priceObj) || priceObj is null)
            return BadRequest("Parâmetros obrigatórios: ShipmentReleaseKey, UsageCode, UnitPrice.");

        try
        {
            var created = await service.ExecuteAsync(
                new StorageRemittanceRequest(key, Convert.ToInt32(usageObj, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(priceObj, CultureInfo.InvariantCulture)),
                User.Identity?.Name ?? "Unknown");
            return Ok(created.Key);
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (DefaultException e) { return BadRequest(e.Message); }
        catch (ApplicationException e) { return BadRequest(e.Message); }
    }
}
