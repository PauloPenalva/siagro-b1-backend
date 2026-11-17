using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// "Vincular NF de devolução" e "Desvincular NF de devolução" (GAC-1226): a NF de devolução do SAP (A/R Credit
/// Memo) no documento de devolução da carga. Os serviços só são registrados em modo SAPB1; fora dele as ações
/// respondem 400.
/// </summary>
public class ShipmentLoadsLinkSapCreditMemoController(IServiceProvider services) : ODataController
{
    private const string SapOnly = "Disponível somente com integração SAP Business One.";

    [HttpPost("odata/ShipmentLoadsLinkSapCreditMemo")]
    public async Task<ActionResult> Link(ODataActionParameters parameters)
    {
        // Resolvido sob demanda: só existe no ramo SAPB1 do Program.
        var service = services.GetService<ShipmentLoadsLinkSapCreditMemoService>();
        if (service == null)
            return BadRequest(SapOnly);

        // ODataActionParameters é nulo quando o corpo não vem.
        if (!TryReadKeys(parameters, out var loadKey, out var invoiceKey)
            || !parameters.TryGetValue("SapDocEntry", out var entryObj) || entryObj is not int docEntry || docEntry <= 0)
            return BadRequest("Selecione a devolução e a NF de devolução do SAP.");

        return await RunAsync(() => service.ExecuteAsync(loadKey, invoiceKey, docEntry, User.Identity?.Name ?? "Unknown"));
    }

    [HttpPost("odata/ShipmentLoadsUnlinkSapCreditMemo")]
    public async Task<ActionResult> Unlink(ODataActionParameters parameters)
    {
        var service = services.GetService<ShipmentLoadsUnlinkSapCreditMemoService>();
        if (service == null)
            return BadRequest(SapOnly);

        if (!TryReadKeys(parameters, out var loadKey, out var invoiceKey))
            return BadRequest("Selecione a devolução a desvincular.");

        return await RunAsync(() => service.ExecuteAsync(loadKey, invoiceKey, User.Identity?.Name ?? "Unknown"));
    }

    private static bool TryReadKeys(ODataActionParameters? parameters, out Guid loadKey, out Guid invoiceKey)
    {
        loadKey = invoiceKey = Guid.Empty;

        return parameters != null
               && parameters.TryGetValue("Key", out var keyObj) && keyObj != null
               && parameters.TryGetValue("SalesInvoiceKey", out var invoiceObj) && invoiceObj != null
               && Guid.TryParse(keyObj.ToString(), out loadKey)
               && Guid.TryParse(invoiceObj.ToString(), out invoiceKey);
    }

    private async Task<ActionResult> RunAsync(Func<Task<Domain.Entities.SalesInvoice>> work)
    {
        try
        {
            var invoice = await work();
            return Ok(new { key = invoice.Key, invoiceNumber = invoice.InvoiceNumber });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound();

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
