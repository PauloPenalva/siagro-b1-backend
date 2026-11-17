using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// "Vincular NF do SAP" (GAC-1226). O serviço só é registrado em modo SAPB1; fora dele a ação
/// responde 400.
/// </summary>
public class ShipmentLoadsLinkSapInvoiceController(IServiceProvider services) : ODataController
{
    [HttpPost("odata/ShipmentLoadsLinkSapInvoice")]
    public async Task<ActionResult> Link(ODataActionParameters parameters)
    {
        // Resolvido sob demanda: só existe no ramo SAPB1 do Program.
        var service = services.GetService<ShipmentLoadsLinkSapInvoiceService>();
        if (service == null)
            return BadRequest("Disponível somente com integração SAP Business One.");

        try
        {
            // ODataActionParameters é nulo quando o corpo não vem.
            if (parameters == null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj == null ||
                !parameters.TryGetValue("SapDocEntries", out var entriesObj) || entriesObj is not IEnumerable entries ||
                !parameters.TryGetValue("SalesShipmentReleaseKey", out var releaseObj) || releaseObj == null)
                return BadRequest("Selecione a nota fiscal do SAP e a liberação de entrega.");

            var invoices = await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                entries.Cast<object?>().Select(x => Convert.ToInt32(x)).ToList(),
                Guid.Parse(releaseObj.ToString()!),
                User.Identity?.Name ?? "Unknown");

            // Um item por NF, na ordem de SapDocEntries; com uma NF o detalhe leva o usuário ao documento.
            return Ok(new
            {
                documents = invoices.Select(i => new { key = i.Key, invoiceNumber = i.InvoiceNumber }).ToList(),
            });
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
