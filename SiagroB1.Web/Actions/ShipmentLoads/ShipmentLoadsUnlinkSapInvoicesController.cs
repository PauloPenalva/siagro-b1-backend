using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// "Desvincular NFs do SAP" (GAC-1226). O serviço só é registrado em modo SAPB1; fora dele a ação
/// responde 400.
/// </summary>
public class ShipmentLoadsUnlinkSapInvoicesController(IServiceProvider services) : ODataController
{
    [HttpPost("odata/ShipmentLoadsUnlinkSapInvoices")]
    public async Task<ActionResult> Unlink(ODataActionParameters parameters)
    {
        // Resolvido sob demanda: só existe no ramo SAPB1 do Program.
        var service = services.GetService<ShipmentLoadsUnlinkSapInvoicesService>();
        if (service == null)
            return BadRequest("Disponível somente com integração SAP Business One.");

        try
        {
            if (parameters == null || !parameters.TryGetValue("Key", out var keyObj) || keyObj == null)
                return BadRequest("Informe a carga.");

            var count = await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), User.Identity?.Name ?? "Unknown");

            return Ok(new { count });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound();

            // A carga tem RowVersion: um faturamento concorrente derruba esta chamada.
            for (var inner = e; inner != null; inner = inner.InnerException)
                if (inner is DbUpdateConcurrencyException)
                    return BadRequest("A carga foi alterada por outro usuário. Atualize e tente novamente.");

            if (e is DefaultException or BusinessException or ApplicationException or FormatException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
